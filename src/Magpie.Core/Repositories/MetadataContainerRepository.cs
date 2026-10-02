using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Patch;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class MetadataContainerRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ISubjectRepository subjectRepository,
    ITransactionManager transactionManager,
    ILogger<MetadataContainerRepository> logger,
    TimeProvider timeProvider) : IMetadataContainerRepository
{
    private readonly TimeProvider _time = timeProvider;

    private const string UserProviderType = "user";

    private static bool IsContainerOnly(MetadataContainerPatch patch)
    {
        return patch with { ContributionIds = null, DateLastRefreshed = null } == new MetadataContainerPatch
        {
            ContainerId = patch.ContainerId
        };
    }

    private static bool HasCreatableContent(MetadataContainerPatch patch)
    {
        var hasScalarValue = !string.IsNullOrEmpty(patch.Title?.Value) ||
            !string.IsNullOrEmpty(patch.OriginalTitle?.Value) ||
            !string.IsNullOrEmpty(patch.SortTitle?.Value) ||
            !string.IsNullOrEmpty(patch.Language?.Value) ||
            !string.IsNullOrEmpty(patch.Overview?.Value) ||
            !string.IsNullOrEmpty(patch.ProductionStatus?.Value) ||
            patch.PublishDate?.Value is not null ||
            patch.Duration?.Value is not null ||
            patch.SequenceNumber?.Value is not null ||
            patch.Established?.Value is not null ||
            patch.Dissolved?.Value is not null ||
            patch.BirthDate?.Value is not null ||
            patch.DeathDate?.Value is not null;

        return hasScalarValue ||
            patch.AlternateTexts?.Add?.Any() == true ||
            patch.Awards?.Add?.Any() == true ||
            patch.ContentRatings?.Add?.Any() == true ||
            patch.CustomMetadata?.Value?.Count > 0 ||
            patch.ExternalIds?.Add?.Any() == true ||
            patch.Genres?.Add?.Any() == true ||
            patch.Images?.Add?.Any() == true ||
            patch.Previews?.Add?.Any() == true ||
            patch.References?.Add?.Any() == true ||
            patch.Relationships?.Add?.Any() == true;
    }

    private static void ApplyScalarPatches(MetadataContainerPatch patch, MetadataContribution contribution)
    {
        if (patch.Title is not null)
        {
            contribution.Title = NullIfEmpty(patch.Title.Value);
        }

        if (patch.OriginalTitle is not null)
        {
            contribution.OriginalTitle = NullIfEmpty(patch.OriginalTitle.Value);
        }

        if (patch.SortTitle is not null)
        {
            contribution.SortTitle = NullIfEmpty(patch.SortTitle.Value);
        }

        if (patch.Language is not null)
        {
            contribution.Language = NullIfEmpty(patch.Language.Value);
        }

        if (patch.Overview is not null)
        {
            contribution.Overview = NullIfEmpty(patch.Overview.Value);
        }

        if (patch.ProductionStatus is not null)
        {
            contribution.ProductionStatus = NullIfEmpty(patch.ProductionStatus.Value);
        }

        if (patch.PublishDate is not null)
        {
            contribution.PublishDate = patch.PublishDate.Value;
        }

        if (patch.Duration is not null)
        {
            contribution.Duration = patch.Duration.Value;
        }

        if (patch.SequenceNumber is not null)
        {
            contribution.SequenceNumber = patch.SequenceNumber.Value;
        }

        if (patch.Established is not null)
        {
            contribution.Established = patch.Established.Value;
        }

        if (patch.Dissolved is not null)
        {
            contribution.Dissolved = patch.Dissolved.Value;
        }

        if (patch.BirthDate is not null)
        {
            contribution.BirthDate = patch.BirthDate.Value;
        }

        if (patch.DeathDate is not null)
        {
            contribution.DeathDate = patch.DeathDate.Value;
        }
    }

    private static async Task ApplyCollectionPatchAsync<TEntity, TPatch>(
        CollectionPatch<TEntity, Guid, TPatch>? collectionPatch,
        DbSet<TEntity> set,
        Expression<Func<TEntity, bool>> belongsToContribution,
        Func<TEntity, Guid> entityId,
        Func<TPatch, Guid> patchId,
        Func<TEntity, int> ordinalOf,
        Action<TEntity, int> setOrdinal,
        Action<TEntity> prepareAdd,
        Action<TEntity, TPatch> applyUpdate,
        string entityName,
        CancellationToken cancellationToken) where TEntity : class
    {
        if (collectionPatch is null)
        {
            return;
        }

        var adds = collectionPatch.Add?.ToList() ?? [];
        var removeIds = collectionPatch.Remove?.ToHashSet() ?? [];
        var updates = collectionPatch.Update?.ToList() ?? [];
        if (adds.Count == 0 && removeIds.Count == 0 && updates.Count == 0)
        {
            return;
        }

        var current = await set.Where(belongsToContribution).ToListAsync(cancellationToken).ConfigureAwait(false);

        var nextOrdinal = current.Count == 0 ? 0 : current.Max(ordinalOf) + 1;
        foreach (var item in adds)
        {
            prepareAdd(item);
            if (ordinalOf(item) == 0)
            {
                setOrdinal(item, nextOrdinal);
            }

            nextOrdinal++;
            set.Add(item);
        }

        var currentById = current.ToDictionary(entityId);

        foreach (var removeId in removeIds)
        {
            if (!currentById.TryGetValue(removeId, out var removed))
            {
                throw new MagpieException($"The metadata changes do not contain {entityName} {removeId}.");
            }

            set.Remove(removed);
        }

        foreach (var update in updates)
        {
            if (!currentById.TryGetValue(patchId(update), out var target))
            {
                throw new MagpieException($"The metadata changes do not contain {entityName} {patchId(update)}.");
            }

            applyUpdate(target, update);
        }
    }

    private static string RequireValue(string? value, string message)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new MagpieException(message) : value;
    }

    private static Uri RequireAbsoluteUrl(Uri? url, string entityName)
    {
        return url is { IsAbsoluteUri: true }
            ? url
            : throw new MagpieException($"{entityName} URL must be a valid absolute URL.");
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public async Task<IEnumerable<MetadataContainer>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.MetadataContainers
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The metadata container repository failed to list metadata containers.");
            throw;
        }
    }

    public async Task<MetadataContainer?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.MetadataContainers
                .AsNoTracking()
                .Include(mc => mc.Contributions).ThenInclude(c => c.AlternateTexts)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Awards)
                .Include(mc => mc.Contributions).ThenInclude(c => c.ContentRatings)
                .Include(mc => mc.Contributions).ThenInclude(c => c.CustomMetadata)
                .Include(mc => mc.Contributions).ThenInclude(c => c.ExternalIds)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Genres)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Images)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Previews)
                .Include(mc => mc.Contributions).ThenInclude(c => c.References)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Relationships)
                .AsSplitQuery()
                .FirstOrDefaultAsync(mc => mc.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The metadata container repository failed to read metadata container {ContainerId}.",
                id);
            throw;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>>
        GetRelationshipTitlesByContentHierarchyItemAsync(
            IReadOnlyCollection<Guid> contentHierarchyItemIds,
            CancellationToken cancellationToken = default)
    {
        if (contentHierarchyItemIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>();
        }

        try
        {
            var rows = await (
                from item in context.ContentHierarchyItems.AsNoTracking()
                where contentHierarchyItemIds.Contains(item.Id)
                from contribution in item.MetadataContainer!.Contributions
                from relationship in contribution.Relationships
                where relationship.RelatedMetadataContainerId != null
                from related in context.MetadataContributions.AsNoTracking()
                    .Where(c => c.MetadataContainerId == relationship.RelatedMetadataContainerId)
                    .OrderBy(c => c.Ordinal)
                    .Take(1)
                where related.Title != null && related.Title != ""
                orderby contribution.Ordinal, relationship.Ordinal
                select new { ItemId = item.Id, relationship.Kind, related.Title })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var grouped = new Dictionary<Guid, Dictionary<string, List<string>>>();
            foreach (var row in rows)
            {
                if (!grouped.TryGetValue(row.ItemId, out var byKind))
                {
                    byKind = new();
                    grouped[row.ItemId] = byKind;
                }

                if (!byKind.TryGetValue(row.Kind, out var titles))
                {
                    titles = [];
                    byKind[row.Kind] = titles;
                }

                titles.Add(row.Title!);
            }

            return grouped.ToDictionary(
                node => node.Key,
                node => (IReadOnlyDictionary<string, IReadOnlyList<string>>)node.Value.ToDictionary(
                    relationship => relationship.Key,
                    relationship => (IReadOnlyList<string>)relationship.Value,
                    StringComparer.Ordinal));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The metadata container repository failed to read relationship titles");
            throw;
        }
    }

    public async Task<MetadataContribution> AddContributionAsync(
        Guid containerId,
        MetadataContribution contribution,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var containerExists = await context.MetadataContainers
                        .AnyAsync(mc => mc.Id == containerId, cancellationToken)
                        .ConfigureAwait(false);

                    if (!containerExists)
                    {
                        throw new InvalidOperationException($"Metadata container {containerId} not found");
                    }

                    contribution.MetadataContainerId = containerId;
                    contribution.CreatedAt = now;
                    contribution.CreatedByUserId = createdByUserId;

                    context.MetadataContributions.Add(contribution);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.System,
                            Operation = "Create",
                            EntityId = contribution.Id.ToString(),
                            EntityType = nameof(MetadataContribution),
                            Detail = $"Added metadata from {contribution.ProviderType}.",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return contribution;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The metadata container repository failed to add a contribution to container {ContainerId}.",
                containerId);
            throw;
        }
    }

    public async Task<MetadataContribution> UpdateContributionAsync(
        Guid containerId,
        Guid existingContributionId,
        MetadataContribution incoming,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var existing = await context.MetadataContributions
                        .FirstOrDefaultAsync(
                            c => c.Id == existingContributionId && c.MetadataContainerId == containerId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (existing is null)
                    {
                        throw new InvalidOperationException(
                            $"Container {containerId} does not contain metadata contribution " +
                            $"{existingContributionId}.");
                    }

                    var primaryImageUrls = await (
                        from e in context.Images
                        where e.MetadataContributionId == existingContributionId && e.IsPrimary
                        select e.Url)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    await DeleteContributionChildrenAsync(existingContributionId, cancellationToken)
                        .ConfigureAwait(false);

                    existing.Ordinal = incoming.Ordinal;
                    existing.ExtensionId = incoming.ExtensionId;
                    existing.ProviderType = incoming.ProviderType;
                    existing.Source = incoming.Source;
                    existing.Title = incoming.Title;
                    existing.OriginalTitle = incoming.OriginalTitle;
                    existing.SortTitle = incoming.SortTitle;
                    existing.Language = incoming.Language;
                    existing.Overview = incoming.Overview;
                    existing.PublishDate = incoming.PublishDate;
                    existing.PublishDatePrecision = incoming.PublishDatePrecision;
                    existing.Duration = incoming.Duration;
                    existing.SequenceNumber = incoming.SequenceNumber;
                    existing.ProductionStatus = incoming.ProductionStatus;
                    existing.Established = incoming.Established;
                    existing.Dissolved = incoming.Dissolved;
                    existing.BirthDate = incoming.BirthDate;
                    existing.DeathDate = incoming.DeathDate;
                    existing.DateLastRefreshed = incoming.DateLastRefreshed ?? now;
                    existing.ModifiedAt = now;
                    existing.ModifiedByUserId = modifiedByUserId;

                    foreach (var child in incoming.AlternateTexts)
                    {
                        child.MetadataContributionId = existingContributionId;
                        context.AlternateTexts.Add(child);
                    }

                    foreach (var child in incoming.Awards)
                    {
                        child.MetadataContributionId = existingContributionId;
                        context.Awards.Add(child);
                    }

                    foreach (var child in incoming.ContentRatings)
                    {
                        child.MetadataContributionId = existingContributionId;
                        context.ContentRatings.Add(child);
                    }

                    foreach (var child in incoming.CustomMetadata)
                    {
                        child.MetadataContributionId = existingContributionId;
                        context.CustomMetadataValues.Add(child);
                    }

                    foreach (var child in incoming.ExternalIds)
                    {
                        child.MetadataContributionId = existingContributionId;
                        context.ExternalIds.Add(child);
                    }

                    foreach (var child in incoming.Genres)
                    {
                        child.MetadataContributionId = existingContributionId;
                        context.Genres.Add(child);
                    }

                    var primaryImage = incoming.Images
                        .Where(i => primaryImageUrls.Contains(i.Url))
                        .OrderBy(i => i.Ordinal)
                        .ThenBy(i => i.Url.AbsoluteUri, StringComparer.Ordinal)
                        .FirstOrDefault();
                    foreach (var child in incoming.Images)
                    {
                        child.MetadataContributionId = existingContributionId;
                        child.IsPrimary = ReferenceEquals(child, primaryImage);
                        context.Images.Add(child);
                    }

                    foreach (var child in incoming.Previews)
                    {
                        child.MetadataContributionId = existingContributionId;
                        context.Previews.Add(child);
                    }

                    foreach (var child in incoming.References)
                    {
                        child.MetadataContributionId = existingContributionId;
                        context.References.Add(child);
                    }

                    foreach (var child in incoming.Relationships)
                    {
                        child.MetadataContributionId = existingContributionId;
                        context.Relationships.Add(child);
                    }

                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return existing;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The metadata container repository failed to update contribution {ContributionId} in container " +
                "{ContainerId}.",
                existingContributionId,
                containerId);
            throw;
        }
    }

    private async Task ApplyContributionOrderAsync(
        Guid containerId,
        IReadOnlyList<Guid> contributionIds,
        Guid? modifiedByUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var contributions = await context.MetadataContributions
            .Where(c => c.MetadataContainerId == containerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var providerContributions = contributions
            .Where(c => !string.Equals(c.ProviderType, UserProviderType, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(c => c.Id);
        if (contributionIds.Count != providerContributions.Count ||
            contributionIds.Distinct().Count() != contributionIds.Count ||
            contributionIds.Any(id => !providerContributions.ContainsKey(id)))
        {
            throw new MagpieException("The provider contribution order no longer matches the metadata container.");
        }

        var changed = false;
        foreach (var userContribution in contributions.Where(c => !providerContributions.ContainsKey(c.Id)))
        {
            if (userContribution.Ordinal != -1)
            {
                userContribution.Ordinal = -1;
                userContribution.ModifiedAt = now;
                userContribution.ModifiedByUserId = modifiedByUserId;
                changed = true;
            }
        }

        for (var ordinal = 0; ordinal < contributionIds.Count; ordinal++)
        {
            var contribution = providerContributions[contributionIds[ordinal]];
            if (contribution.Ordinal == ordinal)
            {
                continue;
            }

            contribution.Ordinal = ordinal;
            contribution.ModifiedAt = now;
            contribution.ModifiedByUserId = modifiedByUserId;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditRecordRepository.CreateAsync(
            new()
            {
                Category = AuditCategories.System,
                Operation = "Update",
                EntityId = containerId.ToString(),
                EntityType = nameof(MetadataContainer),
                Detail = "Reordered the provider metadata contributions.",
                Timestamp = now,
                UserId = modifiedByUserId
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ApplyPrimaryImagePatchAsync(
        Guid containerId,
        Guid imageId,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var belongs = await context.Images
                        .Join(
                            context.MetadataContributions,
                            i => i.MetadataContributionId,
                            c => c.Id,
                            (i, c) => new { i.Id, c.MetadataContainerId })
                        .AnyAsync(x => x.Id == imageId && x.MetadataContainerId == containerId, cancellationToken)
                        .ConfigureAwait(false);

                    if (!belongs)
                    {
                        return false;
                    }

                    var containerImageIds =
                        from i in context.Images
                        where context.MetadataContributions
                            .Any(c => c.Id == i.MetadataContributionId && c.MetadataContainerId == containerId)
                        select i.Id;

                    await context.Images
                        .Where(i => containerImageIds.Contains(i.Id) && i.Id != imageId && i.IsPrimary)
                        .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsPrimary, false), cancellationToken)
                        .ConfigureAwait(false);

                    await context.Images
                        .Where(i => i.Id == imageId)
                        .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsPrimary, true), cancellationToken)
                        .ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.System,
                            Operation = "Update",
                            EntityId = imageId.ToString(),
                            EntityType = nameof(Image),
                            Detail = $"Set the primary image for item {containerId}.",
                            Timestamp = _time.GetUtcNow().UtcDateTime,
                            UserId = modifiedByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The metadata container repository failed to set primary image {ImageId} for container {ContainerId}.",
                imageId,
                containerId);
            throw;
        }
    }

    public async Task<IReadOnlyList<Uri>> ListPrimaryImageUrlsAsync(
        IReadOnlyCollection<Guid> contributionIds,
        CancellationToken cancellationToken)
    {
        if (contributionIds.Count == 0)
        {
            return [];
        }

        try
        {
            return await (
                from i in context.Images.AsNoTracking()
                where contributionIds.Contains(i.MetadataContributionId) && i.IsPrimary
                select i.Url)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The metadata container repository failed to list primary image URLs.");
            throw;
        }
    }

    public async Task<IReadOnlyList<MetadataRelationshipKey>> ListRelationshipKeysAsync(
        Guid contributionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await (
                from r in context.Relationships.AsNoTracking()
                where r.MetadataContributionId == contributionId
                select new { r.RelatedMetadataContainerId, r.Kind, r.Qualifier })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return
            [.. rows.Select(r => new MetadataRelationshipKey(r.RelatedMetadataContainerId, r.Kind, r.Qualifier))];
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The metadata container repository failed to list relationship keys for contribution {ContributionId}.",
                contributionId);
            throw;
        }
    }

    public async Task AddRelationshipAsync(Relationship relationship, CancellationToken cancellationToken)
    {
        try
        {
            context.Relationships.Add(relationship);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The metadata container repository failed to add a relationship for contribution {ContributionId}.",
                relationship.MetadataContributionId);
            throw;
        }
    }

    public async Task RemoveContributionsAsync(
        IReadOnlyList<MetadataContribution> contributions,
        Guid? deletedByUserId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        if (contributions.Count == 0)
        {
            return;
        }

        try
        {
            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var contributionIds = contributions.Select(c => c.Id).ToArray();

                    await context.AlternateTexts
                        .Where(e => contributionIds.Contains(e.MetadataContributionId))
                        .ExecuteDeleteAsync(ct)
                        .ConfigureAwait(false);
                    await context.Awards
                        .Where(e => contributionIds.Contains(e.MetadataContributionId))
                        .ExecuteDeleteAsync(ct)
                        .ConfigureAwait(false);
                    await context.ContentRatings
                        .Where(e => contributionIds.Contains(e.MetadataContributionId))
                        .ExecuteDeleteAsync(ct)
                        .ConfigureAwait(false);
                    await context.CustomMetadataValues
                        .Where(e => contributionIds.Contains(e.MetadataContributionId))
                        .ExecuteDeleteAsync(ct)
                        .ConfigureAwait(false);
                    await context.ExternalIds
                        .Where(e => e.MetadataContributionId.HasValue &&
                            contributionIds.Contains(e.MetadataContributionId.Value))
                        .ExecuteDeleteAsync(ct)
                        .ConfigureAwait(false);
                    await context.Genres
                        .Where(e => contributionIds.Contains(e.MetadataContributionId))
                        .ExecuteDeleteAsync(ct)
                        .ConfigureAwait(false);
                    await context.Images
                        .Where(e => contributionIds.Contains(e.MetadataContributionId))
                        .ExecuteDeleteAsync(ct)
                        .ConfigureAwait(false);
                    await context.Previews
                        .Where(e => contributionIds.Contains(e.MetadataContributionId))
                        .ExecuteDeleteAsync(ct)
                        .ConfigureAwait(false);
                    await context.References
                        .Where(e => contributionIds.Contains(e.MetadataContributionId))
                        .ExecuteDeleteAsync(ct)
                        .ConfigureAwait(false);
                    await context.Relationships
                        .Where(e => contributionIds.Contains(e.MetadataContributionId))
                        .ExecuteDeleteAsync(ct)
                        .ConfigureAwait(false);
                    await context.MetadataContributions
                        .Where(e => contributionIds.Contains(e.Id))
                        .ExecuteDeleteAsync(ct)
                        .ConfigureAwait(false);

                    var now = _time.GetUtcNow().UtcDateTime;
                    foreach (var contribution in contributions)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Delete",
                                EntityId = contribution.Id.ToString(),
                                EntityType = nameof(MetadataContribution),
                                Detail = $"Deleted metadata from {contribution.ProviderType}.",
                                Timestamp = now,
                                UserId = deletedByUserId,
                                CorrelationId = correlationId
                            },
                            ct).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The metadata container repository failed to remove {ContributionCount} contributions.",
                contributions.Count);
            throw;
        }
    }

    public async Task<IReadOnlyList<Guid>> ListOrphanedContainerIdsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await (
                from container in context.MetadataContainers.AsNoTracking()
                where !context.ContentHierarchyItems.Any(item => item.MetadataContainerId == container.Id)
                where !context.Subjects.Any(subject => subject.MetadataContainerId == container.Id)
                orderby container.CreatedAt, container.Id
                select container.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The metadata container repository failed to list orphaned metadata containers.");
            throw;
        }
    }

    public async Task RemoveContainersAsync(IReadOnlyList<Guid> containerIds, CancellationToken cancellationToken)
    {
        if (containerIds.Count == 0)
        {
            return;
        }

        try
        {
            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    await context.Set<Relationship>()
                        .Where(r => r.RelatedMetadataContainerId != null &&
                            containerIds.Contains(r.RelatedMetadataContainerId.Value))
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.RelatedMetadataContainerId, (Guid?)null), ct)
                        .ConfigureAwait(false);

                    var contributionIds = await (
                        from c in context.MetadataContributions.AsNoTracking()
                        where containerIds.Contains(c.MetadataContainerId)
                        select c.Id)
                        .ToListAsync(ct)
                        .ConfigureAwait(false);

                    if (contributionIds.Count > 0)
                    {
                        await context.AlternateTexts
                            .Where(e => contributionIds.Contains(e.MetadataContributionId))
                            .ExecuteDeleteAsync(ct)
                            .ConfigureAwait(false);
                        await context.Awards
                            .Where(e => contributionIds.Contains(e.MetadataContributionId))
                            .ExecuteDeleteAsync(ct)
                            .ConfigureAwait(false);
                        await context.ContentRatings
                            .Where(e => contributionIds.Contains(e.MetadataContributionId))
                            .ExecuteDeleteAsync(ct)
                            .ConfigureAwait(false);
                        await context.CustomMetadataValues
                            .Where(e => contributionIds.Contains(e.MetadataContributionId))
                            .ExecuteDeleteAsync(ct)
                            .ConfigureAwait(false);
                        await context.ExternalIds
                            .Where(e => e.MetadataContributionId.HasValue &&
                                contributionIds.Contains(e.MetadataContributionId.Value))
                            .ExecuteDeleteAsync(ct)
                            .ConfigureAwait(false);
                        await context.Genres
                            .Where(e => contributionIds.Contains(e.MetadataContributionId))
                            .ExecuteDeleteAsync(ct)
                            .ConfigureAwait(false);
                        await context.Images
                            .Where(e => contributionIds.Contains(e.MetadataContributionId))
                            .ExecuteDeleteAsync(ct)
                            .ConfigureAwait(false);
                        await context.Previews
                            .Where(e => contributionIds.Contains(e.MetadataContributionId))
                            .ExecuteDeleteAsync(ct)
                            .ConfigureAwait(false);
                        await context.References
                            .Where(e => contributionIds.Contains(e.MetadataContributionId))
                            .ExecuteDeleteAsync(ct)
                            .ConfigureAwait(false);
                        await context.Relationships
                            .Where(e => contributionIds.Contains(e.MetadataContributionId))
                            .ExecuteDeleteAsync(ct)
                            .ConfigureAwait(false);
                        await context.MetadataContributions
                            .Where(e => contributionIds.Contains(e.Id))
                            .ExecuteDeleteAsync(ct)
                            .ConfigureAwait(false);
                    }

                    await context.MetadataContainers
                        .Where(c => containerIds.Contains(c.Id))
                        .ExecuteDeleteAsync(ct)
                        .ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The metadata container repository failed to remove {ContainerCount} containers.",
                containerIds.Count);
            throw;
        }
    }

    private async Task DeleteContributionChildrenAsync(Guid contributionId, CancellationToken cancellationToken)
    {
        await context.AlternateTexts
            .Where(e => e.MetadataContributionId == contributionId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Awards
            .Where(e => e.MetadataContributionId == contributionId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.ContentRatings
            .Where(e => e.MetadataContributionId == contributionId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.CustomMetadataValues
            .Where(e => e.MetadataContributionId == contributionId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.ExternalIds
            .Where(e => e.MetadataContributionId == contributionId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Genres
            .Where(e => e.MetadataContributionId == contributionId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Images
            .Where(e => e.MetadataContributionId == contributionId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Previews
            .Where(e => e.MetadataContributionId == contributionId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.References
            .Where(e => e.MetadataContributionId == contributionId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.Relationships
            .Where(e => e.MetadataContributionId == contributionId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<MetadataContribution> ResolveOrCreateUserContributionAsync(
        Guid containerId,
        Guid? createdByUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await context.MetadataContributions
            .FirstOrDefaultAsync(
                c => c.MetadataContainerId == containerId && c.ProviderType == UserProviderType,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var hasAny = await context.MetadataContributions
            .AnyAsync(c => c.MetadataContainerId == containerId, cancellationToken)
            .ConfigureAwait(false);
        var minOrdinal = hasAny
            ? await context.MetadataContributions
                .Where(c => c.MetadataContainerId == containerId)
                .MinAsync(c => c.Ordinal, cancellationToken)
                .ConfigureAwait(false)
            : 0;

        var contribution = new MetadataContribution
        {
            MetadataContainerId = containerId,
            ProviderType = UserProviderType,
            Source = UserProviderType,
            Ordinal = minOrdinal - 1,
            CreatedAt = now,
            CreatedByUserId = createdByUserId
        };
        context.MetadataContributions.Add(contribution);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return contribution;
    }

    public async Task<bool> PatchAsync(
        MetadataContainerPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var containerExists = await context.MetadataContainers
                        .AnyAsync(mc => mc.Id == patch.ContainerId, ct)
                        .ConfigureAwait(false);
                    if (!containerExists)
                    {
                        return false;
                    }

                    if (patch.DateLastRefreshed is not null)
                    {
                        await context.MetadataContainers
                            .Where(c => c.Id == patch.ContainerId)
                            .ExecuteUpdateAsync(
                                s => s.SetProperty(c => c.DateLastRefreshed, patch.DateLastRefreshed.Value),
                                ct)
                            .ConfigureAwait(false);
                        if (patch.ContributionIds is null && IsContainerOnly(patch))
                        {
                            return true;
                        }
                    }

                    if (patch.PrimaryImageId is not null && !await ApplyPrimaryImagePatchAsync(
                        patch.ContainerId,
                        patch.PrimaryImageId.Value,
                        modifiedByUserId,
                        ct).ConfigureAwait(false))
                    {
                        return false;
                    }

                    if (patch.ContributionIds is { } contributionOrder)
                    {
                        await ApplyContributionOrderAsync(
                            patch.ContainerId,
                            contributionOrder,
                            modifiedByUserId,
                            now,
                            ct).ConfigureAwait(false);
                        if (IsContainerOnly(patch))
                        {
                            return true;
                        }
                    }

                    var existing = await context.MetadataContributions
                        .FirstOrDefaultAsync(
                            c => c.MetadataContainerId == patch.ContainerId && c.ProviderType == UserProviderType,
                            ct)
                        .ConfigureAwait(false);
                    if (existing is null && !HasCreatableContent(patch))
                    {
                        return true;
                    }

                    var contribution = existing
                        ?? await ResolveOrCreateUserContributionAsync(
                            patch.ContainerId,
                            modifiedByUserId,
                            now,
                            ct).ConfigureAwait(false);

                    ApplyScalarPatches(patch, contribution);
                    await ApplySubItemPatchesAsync(patch, contribution.Id, ct).ConfigureAwait(false);
                    await ApplyGenrePatchAsync(patch.Genres, contribution.Id, ct).ConfigureAwait(false);
                    await ApplyCustomMetadataPatchAsync(patch.CustomMetadata, contribution.Id, ct).ConfigureAwait(
                        false);
                    await ApplyRelationshipPatchAsync(patch.Relationships, contribution.Id, modifiedByUserId, ct)
                        .ConfigureAwait(false);

                    contribution.ModifiedAt = now;
                    contribution.ModifiedByUserId = modifiedByUserId;
                    await context.SaveChangesAsync(ct).ConfigureAwait(false);

                    var cleared = await IsUserContributionEmptyAsync(contribution, ct).ConfigureAwait(false);
                    if (cleared)
                    {
                        await DeleteContributionChildrenAsync(contribution.Id, ct).ConfigureAwait(false);
                        await context.MetadataContributions
                            .Where(c => c.Id == contribution.Id)
                            .ExecuteDeleteAsync(ct)
                            .ConfigureAwait(false);
                    }

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.System,
                            Operation = cleared ? "Delete" : existing is null ? "Create" : "Patch",
                            EntityId = contribution.Id.ToString(),
                            EntityType = nameof(MetadataContribution),
                            Detail = cleared
                                    ? "Cleared user metadata contribution"
                                    : "Patched user metadata contribution",
                            Timestamp = now,
                            UserId = modifiedByUserId
                        },
                        ct).ConfigureAwait(false);

                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The metadata container repository failed to patch container {ContainerId}.",
                patch.ContainerId);
            throw;
        }
    }

    private async Task ApplySubItemPatchesAsync(
        MetadataContainerPatch patch,
        Guid contributionId,
        CancellationToken cancellationToken)
    {
        await ApplyCollectionPatchAsync(
            patch.AlternateTexts,
            context.AlternateTexts,
            e => e.MetadataContributionId == contributionId,
            e => e.Id,
            p => p.Id,
            e => e.Ordinal,
            (e, o) => e.Ordinal = o,
            e =>
            {
                e.Id = e.Id == Guid.Empty ? Guid.NewGuid() : e.Id;
                e.MetadataContributionId = contributionId;
                RequireValue(e.Kind, "AlternateText 'kind' is required.");
                RequireValue(e.Text, "AlternateText 'text' is required.");
            },
            (e, p) =>
            {
                if (p.Ordinal is not null)
                {
                    e.Ordinal = p.Ordinal.Value;
                }

                if (p.Kind is not null)
                {
                    e.Kind = RequireValue(p.Kind.Value, "AlternateText 'kind' is required.");
                }

                if (p.Language is not null)
                {
                    e.Language = NullIfEmpty(p.Language.Value);
                }

                if (p.Text is not null)
                {
                    e.Text = RequireValue(p.Text.Value, "AlternateText 'text' is required.");
                }
            },
            nameof(AlternateText),
            cancellationToken).ConfigureAwait(false);

        await ApplyCollectionPatchAsync(
            patch.Awards,
            context.Awards,
            e => e.MetadataContributionId == contributionId,
            e => e.Id,
            p => p.Id,
            e => e.Ordinal,
            (e, o) => e.Ordinal = o,
            e =>
            {
                e.Id = e.Id == Guid.Empty ? Guid.NewGuid() : e.Id;
                e.MetadataContributionId = contributionId;
                RequireValue(e.Body, "Award 'body' is required.");
                RequireValue(e.Name, "Award 'name' is required.");
            },
            (e, p) =>
            {
                if (p.Ordinal is not null)
                {
                    e.Ordinal = p.Ordinal.Value;
                }

                if (p.Body is not null)
                {
                    e.Body = RequireValue(p.Body.Value, "Award 'body' is required.");
                }

                if (p.Name is not null)
                {
                    e.Name = RequireValue(p.Name.Value, "Award 'name' is required.");
                }

                if (p.Category is not null)
                {
                    e.Category = NullIfEmpty(p.Category.Value);
                }

                if (p.Status is not null)
                {
                    e.Status = NullIfEmpty(p.Status.Value);
                }

                if (p.Year is not null)
                {
                    e.Year = p.Year.Value;
                }

                if (p.Language is not null)
                {
                    e.Language = NullIfEmpty(p.Language.Value);
                }

                if (p.Country is not null)
                {
                    e.Country = NullIfEmpty(p.Country.Value);
                }
            },
            nameof(Award),
            cancellationToken).ConfigureAwait(false);

        await ApplyCollectionPatchAsync(
            patch.ContentRatings,
            context.ContentRatings,
            e => e.MetadataContributionId == contributionId,
            e => e.Id,
            p => p.Id,
            e => e.Ordinal,
            (e, o) => e.Ordinal = o,
            e =>
            {
                e.Id = e.Id == Guid.Empty ? Guid.NewGuid() : e.Id;
                e.MetadataContributionId = contributionId;
                RequireValue(e.Body, "ContentRating 'body' is required.");
                RequireValue(e.Rating, "ContentRating 'rating' is required.");
            },
            (e, p) =>
            {
                if (p.Ordinal is not null)
                {
                    e.Ordinal = p.Ordinal.Value;
                }

                if (p.Body is not null)
                {
                    e.Body = RequireValue(p.Body.Value, "ContentRating 'body' is required.");
                }

                if (p.Rating is not null)
                {
                    e.Rating = RequireValue(p.Rating.Value, "ContentRating 'rating' is required.");
                }
            },
            nameof(ContentRating),
            cancellationToken).ConfigureAwait(false);

        await ApplyCollectionPatchAsync(
            patch.ExternalIds,
            context.ExternalIds,
            e => e.MetadataContributionId == contributionId,
            e => e.Id,
            p => p.Id,
            e => e.Ordinal,
            (e, o) => e.Ordinal = o,
            e =>
            {
                e.Id = e.Id == Guid.Empty ? Guid.NewGuid() : e.Id;
                e.MetadataContributionId = contributionId;
                e.ReleaseId = null;
                RequireValue(e.Value, "ExternalId 'value' is required.");
            },
            (e, p) =>
            {
                if (p.Ordinal is not null)
                {
                    e.Ordinal = p.Ordinal.Value;
                }

                if (p.Value is not null)
                {
                    e.Value = RequireValue(p.Value.Value, "ExternalId 'value' is required.");
                }

                if (p.Source is not null)
                {
                    e.Source = NullIfEmpty(p.Source.Value);
                }
            },
            nameof(ExternalId),
            cancellationToken).ConfigureAwait(false);

        await ApplyCollectionPatchAsync(
            patch.Images,
            context.Images,
            e => e.MetadataContributionId == contributionId,
            e => e.Id,
            p => p.Id,
            e => e.Ordinal,
            (e, o) => e.Ordinal = o,
            e =>
            {
                e.Id = e.Id == Guid.Empty ? Guid.NewGuid() : e.Id;
                e.MetadataContributionId = contributionId;
                e.IsPrimary = false;
                RequireValue(e.Type, "Image 'type' is required.");
                RequireAbsoluteUrl(e.Url, nameof(Image));
            },
            (e, p) =>
            {
                if (p.Ordinal is not null)
                {
                    e.Ordinal = p.Ordinal.Value;
                }

                if (p.Type is not null)
                {
                    e.Type = RequireValue(p.Type.Value, "Image 'type' is required.");
                }

                if (p.MimeType is not null)
                {
                    e.MimeType = NullIfEmpty(p.MimeType.Value);
                }

                if (p.Url is not null)
                {
                    e.Url = RequireAbsoluteUrl(p.Url.Value, nameof(Image));
                }

                if (p.Source is not null)
                {
                    e.Source = NullIfEmpty(p.Source.Value);
                }

                if (p.Language is not null)
                {
                    e.Language = NullIfEmpty(p.Language.Value);
                }

                if (p.Width is not null)
                {
                    e.Width = p.Width.Value;
                }

                if (p.Height is not null)
                {
                    e.Height = p.Height.Value;
                }
            },
            nameof(Image),
            cancellationToken).ConfigureAwait(false);

        await ApplyCollectionPatchAsync(
            patch.Previews,
            context.Previews,
            e => e.MetadataContributionId == contributionId,
            e => e.Id,
            p => p.Id,
            e => e.Ordinal,
            (e, o) => e.Ordinal = o,
            e =>
            {
                e.Id = e.Id == Guid.Empty ? Guid.NewGuid() : e.Id;
                e.MetadataContributionId = contributionId;
                RequireValue(e.Kind, "Preview 'kind' is required.");
                RequireAbsoluteUrl(e.Url, nameof(Preview));
            },
            (e, p) =>
            {
                if (p.Ordinal is not null)
                {
                    e.Ordinal = p.Ordinal.Value;
                }

                if (p.Kind is not null)
                {
                    e.Kind = RequireValue(p.Kind.Value, "Preview 'kind' is required.");
                }

                if (p.MimeType is not null)
                {
                    e.MimeType = NullIfEmpty(p.MimeType.Value);
                }

                if (p.Url is not null)
                {
                    e.Url = RequireAbsoluteUrl(p.Url.Value, nameof(Preview));
                }

                if (p.Source is not null)
                {
                    e.Source = NullIfEmpty(p.Source.Value);
                }

                if (p.Title is not null)
                {
                    e.Title = NullIfEmpty(p.Title.Value);
                }

                if (p.ReleaseDate is not null)
                {
                    e.ReleaseDate = p.ReleaseDate.Value;
                }

                if (p.Language is not null)
                {
                    e.Language = NullIfEmpty(p.Language.Value);
                }
            },
            nameof(Preview),
            cancellationToken).ConfigureAwait(false);

        await ApplyCollectionPatchAsync(
            patch.References,
            context.References,
            e => e.MetadataContributionId == contributionId,
            e => e.Id,
            p => p.Id,
            e => e.Ordinal,
            (e, o) => e.Ordinal = o,
            e =>
            {
                e.Id = e.Id == Guid.Empty ? Guid.NewGuid() : e.Id;
                e.MetadataContributionId = contributionId;
                RequireValue(e.Kind, "Reference 'kind' is required.");
                RequireValue(e.Source, "Reference 'source' is required.");
                RequireAbsoluteUrl(e.Url, nameof(Reference));
            },
            (e, p) =>
            {
                if (p.Ordinal is not null)
                {
                    e.Ordinal = p.Ordinal.Value;
                }

                if (p.Kind is not null)
                {
                    e.Kind = RequireValue(p.Kind.Value, "Reference 'kind' is required.");
                }

                if (p.Source is not null)
                {
                    e.Source = RequireValue(p.Source.Value, "Reference 'source' is required.");
                }

                if (p.Url is not null)
                {
                    e.Url = RequireAbsoluteUrl(p.Url.Value, nameof(Reference));
                }

                if (p.DisplayName is not null)
                {
                    e.DisplayName = NullIfEmpty(p.DisplayName.Value);
                }

                if (p.Language is not null)
                {
                    e.Language = NullIfEmpty(p.Language.Value);
                }
            },
            nameof(Reference),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyGenrePatchAsync(
        SetPatch<string, string>? genrePatch,
        Guid contributionId,
        CancellationToken cancellationToken)
    {
        if (genrePatch is null)
        {
            return;
        }

        var genres = await context.Genres
            .Where(g => g.MetadataContributionId == contributionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (genrePatch.Remove is not null)
        {
            var removeNames = genrePatch.Remove.ToHashSet(StringComparer.Ordinal);
            context.Genres.RemoveRange(genres.Where(g => removeNames.Contains(g.Name)));
        }

        if (genrePatch.Add is not null)
        {
            var existingNames = genres.Select(g => g.Name).ToHashSet(StringComparer.Ordinal);
            var nextOrdinal = genres.Count == 0 ? 0 : genres.Max(g => g.Ordinal) + 1;
            foreach (var name in genrePatch.Add)
            {
                var genreName = RequireValue(name, "Genre 'name' is required.");
                if (!existingNames.Add(genreName))
                {
                    continue;
                }

                context.Genres.Add(new Genre
                {
                    Id = Guid.NewGuid(),
                    MetadataContributionId = contributionId,
                    Name = genreName,
                    Ordinal = nextOrdinal++
                });
            }
        }
    }

    private async Task ApplyCustomMetadataPatchAsync(
        ScalarPatch<IReadOnlyDictionary<string, string?>>? customMetadataPatch,
        Guid contributionId,
        CancellationToken cancellationToken)
    {
        if (customMetadataPatch is null)
        {
            return;
        }

        var existingValues = await context.CustomMetadataValues
            .Where(v => v.MetadataContributionId == contributionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        context.CustomMetadataValues.RemoveRange(existingValues);

        if (customMetadataPatch.Value is null)
        {
            return;
        }

        foreach (var (key, value) in customMetadataPatch.Value)
        {
            var customKey = RequireValue(key, "CustomMetadataValue 'key' is required.");
            context.CustomMetadataValues.Add(new CustomMetadataValue
            {
                Id = Guid.NewGuid(),
                MetadataContributionId = contributionId,
                Key = customKey,
                Value = value
            });
        }
    }

    private async Task ApplyRelationshipPatchAsync(
        CollectionPatch<RelationshipPatch, Guid, RelationshipPatch>? relationshipPatch,
        Guid contributionId,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        if (relationshipPatch is null)
        {
            return;
        }

        var relationships = await context.Relationships
            .Where(r => r.MetadataContributionId == contributionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (relationshipPatch.Remove is not null)
        {
            var removeIds = relationshipPatch.Remove.ToHashSet();
            context.Relationships.RemoveRange(relationships.Where(r => removeIds.Contains(r.Id)));
        }

        if (relationshipPatch.Add is not null)
        {
            var nextOrdinal = relationships.Count == 0 ? 0 : relationships.Max(r => r.Ordinal) + 1;
            foreach (var add in relationshipPatch.Add)
            {
                var kind = RequireValue(add.Kind?.Value, "Relationship 'kind' is required.");
                var relatedContainerId = await ResolveRelatedSubjectContainerAsync(
                    add,
                    modifiedByUserId,
                    cancellationToken).ConfigureAwait(false);
                context.Relationships.Add(new Relationship
                {
                    Id = add.Id == Guid.Empty ? Guid.NewGuid() : add.Id,
                    MetadataContributionId = contributionId,
                    Kind = kind,
                    Qualifier = NullIfEmpty(add.Qualifier?.Value),
                    Ordinal = add.Ordinal?.Value ?? nextOrdinal++,
                    RelatedMetadataContainerId = relatedContainerId
                });
            }
        }

        if (relationshipPatch.Update is not null)
        {
            foreach (var update in relationshipPatch.Update)
            {
                var target = relationships.FirstOrDefault(r => r.Id == update.Id)
                    ?? throw new MagpieException(
                        $"The metadata changes do not contain {nameof(Relationship)} {update.Id}.");

                if (update.Kind is not null)
                {
                    target.Kind = RequireValue(update.Kind.Value, "Relationship 'kind' is required.");
                }

                if (update.Qualifier is not null)
                {
                    target.Qualifier = NullIfEmpty(update.Qualifier.Value);
                }

                if (update.Ordinal is not null)
                {
                    target.Ordinal = update.Ordinal.Value;
                }

                if (update.SubjectId is not null ||
                    !string.IsNullOrWhiteSpace(update.SubjectName) ||
                    !string.IsNullOrWhiteSpace(update.SubjectKind))
                {
                    target.RelatedMetadataContainerId = await ResolveRelatedSubjectContainerAsync(
                        update,
                        modifiedByUserId,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task<Guid?> ResolveRelatedSubjectContainerAsync(
        RelationshipPatch relationshipPatch,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (relationshipPatch.SubjectId is { } subjectId)
        {
            var subject = await subjectRepository.GetByIdAsync(subjectId, cancellationToken).ConfigureAwait(false)
                ?? throw new MagpieException($"Subject {subjectId} does not exist.");
            return subject.MetadataContainerId;
        }

        if (!string.IsNullOrWhiteSpace(relationshipPatch.SubjectName) &&
            !string.IsNullOrWhiteSpace(relationshipPatch.SubjectKind))
        {
            var container = new MetadataContainer { Id = Guid.NewGuid() };
            container.Contributions.Add(new()
            {
                Id = Guid.NewGuid(),
                ProviderType = UserProviderType,
                Source = UserProviderType,
                Ordinal = -1,
                Title = relationshipPatch.SubjectName
            });
            var created = await subjectRepository.CreateAsync(
                new Subject
                {
                    Id = Guid.NewGuid(),
                    Kind = relationshipPatch.SubjectKind!,
                    MetadataContainer = container
                },
                userId,
                cancellationToken).ConfigureAwait(false);
            return created.MetadataContainerId;
        }

        throw new MagpieException("The relationship names neither an existing subject nor a subject name and kind.");
    }

    private async Task<bool> IsUserContributionEmptyAsync(
        MetadataContribution contribution,
        CancellationToken cancellationToken)
    {
        var hasScalar = contribution.Title is not null ||
            contribution.OriginalTitle is not null ||
            contribution.SortTitle is not null ||
            contribution.Language is not null ||
            contribution.Overview is not null ||
            contribution.PublishDate is not null ||
            contribution.Duration is not null ||
            contribution.SequenceNumber is not null ||
            contribution.ProductionStatus is not null ||
            contribution.Established is not null ||
            contribution.Dissolved is not null ||
            contribution.BirthDate is not null ||
            contribution.DeathDate is not null;
        if (hasScalar)
        {
            return false;
        }

        var id = contribution.Id;
        return !await context.AlternateTexts.AnyAsync(e => e.MetadataContributionId == id, cancellationToken)
                .ConfigureAwait(false) &&
            !await context.Awards.AnyAsync(e => e.MetadataContributionId == id, cancellationToken)
                .ConfigureAwait(false) &&
            !await context.ContentRatings.AnyAsync(e => e.MetadataContributionId == id, cancellationToken)
                .ConfigureAwait(false) &&
            !await context.CustomMetadataValues.AnyAsync(e => e.MetadataContributionId == id, cancellationToken)
                .ConfigureAwait(false) &&
            !await context.ExternalIds.AnyAsync(e => e.MetadataContributionId == id, cancellationToken)
                .ConfigureAwait(false) &&
            !await context.Genres.AnyAsync(e => e.MetadataContributionId == id, cancellationToken)
                .ConfigureAwait(false) &&
            !await context.Images.AnyAsync(e => e.MetadataContributionId == id, cancellationToken)
                .ConfigureAwait(false) &&
            !await context.Previews.AnyAsync(e => e.MetadataContributionId == id, cancellationToken)
                .ConfigureAwait(false) &&
            !await context.References.AnyAsync(e => e.MetadataContributionId == id, cancellationToken)
                .ConfigureAwait(false) &&
            !await context.Relationships.AnyAsync(e => e.MetadataContributionId == id, cancellationToken)
                .ConfigureAwait(false);
    }
}
