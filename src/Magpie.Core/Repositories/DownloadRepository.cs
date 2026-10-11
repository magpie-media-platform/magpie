using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core.Events;
using Magpie.Core.Observability;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ContentEventImpact = Magpie.Sdk.Events.ContentEventImpact;

namespace Magpie.Core.Repositories;

internal sealed class DownloadRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<DownloadRepository> logger,
    IDataProtectionService protector,
    TimeProvider timeProvider) : IDownloadRepository
{
    private static readonly string[] FullUpdateFields =
    [
        "releaseId",
        "downloadClientId",
        "externalId",
        "title",
        "status",
        "outputPath",
        "statusReason",
        "dateCompleted",
        "heldByPolicy",
        "progress",
        "lastProgressAt",
        "failureReason",
        "failedAt",
        "clientRemovedAt",
    ];

    private static readonly string[] RefreshedFields =
    [
        "status",
        "statusReason",
        "outputPath",
        "dateCompleted",
        "progress",
        "lastProgressAt",
        "heldByPolicy",
        "failureReason",
        "failedAt",
    ];

    private readonly TimeProvider _time = timeProvider;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Download>> ListTransferAttemptsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var target = context.Downloads.AsNoTracking().Where(download => download.Id == id);
            return await context.Downloads.AsNoTracking()
                .Where(download => target.Any(attempt => download.Id == attempt.Id ||
                    attempt.ExternalId != null && attempt.ExternalId != "" &&
                    download.DownloadClientId == attempt.DownloadClientId && download.ExternalId == attempt.ExternalId))
                .OrderBy(download => download.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to list transfer attempts for {DownloadId}.", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListFulfilledIdsAsync(
        Guid importedDownloadId,
        Guid? downloadId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var importedScopeIds = await context.DownloadScopes.AsNoTracking()
                .Where(scope => scope.DownloadId == importedDownloadId &&
                    scope.Download!.Status == DownloadStatus.Imported)
                .Select(scope => scope.ContentHierarchyItemId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (importedScopeIds.Count == 0)
            {
                return [];
            }

            var overlappingNodeIds = await ListOverlappingNodeIdsAsync(importedScopeIds, cancellationToken)
                .ConfigureAwait(false);
            var candidateIds = await context.Downloads.AsNoTracking()
                .Where(download => download.Id != importedDownloadId &&
                    (downloadId == null || download.Id == downloadId) &&
                    download.Status != DownloadStatus.Imported && download.Status != DownloadStatus.Importing &&
                    download.Status != DownloadStatus.Failed && download.Status != DownloadStatus.Faulted &&
                    download.Status != DownloadStatus.Cancelled &&
                    context.DownloadScopes.Any(scope => scope.DownloadId == download.Id &&
                        overlappingNodeIds.Contains(scope.ContentHierarchyItemId)))
                .OrderBy(download => download.Id)
                .Select(download => download.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var eligible = new List<Guid>();
            var examined = new HashSet<Guid>();
            foreach (var candidateId in candidateIds)
            {
                if (examined.Contains(candidateId))
                {
                    continue;
                }

                var attempts = await ListTransferAttemptsAsync(candidateId, cancellationToken).ConfigureAwait(false);
                var attemptIds = attempts.Select(attempt => attempt.Id).ToList();
                examined.UnionWith(attemptIds);
                if (attempts.Any(attempt => attempt.Status is DownloadStatus.Imported or DownloadStatus.Importing))
                {
                    continue;
                }

                var scopes = await context.DownloadScopes.AsNoTracking()
                    .Where(scope => attemptIds.Contains(scope.DownloadId))
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                if (attemptIds.Any(attemptId => !scopes.Any(scope => scope.DownloadId == attemptId)))
                {
                    continue;
                }

                var scopeIds = scopes.Select(scope => scope.ContentHierarchyItemId).Distinct().ToList();
                var nodes = await ListFulfillmentScopeNodesAsync(scopeIds, cancellationToken).ConfigureAwait(false);
                var containers = nodes.Values.Where(node => node.ParentId is not null)
                    .Select(node => node.ParentId!.Value).ToHashSet();
                var leaves = nodes.Values.Where(node => !containers.Contains(node.Id)).ToList();
                if (scopeIds.Count > 0 && scopeIds.All(nodes.ContainsKey) && leaves.Count > 0 &&
                    nodes.Values.All(node => node.Fulfillment is null or
                        FulfillmentStatus.Fulfilled or FulfillmentStatus.FulfilledExternally) &&
                    leaves.All(node => node.Fulfillment is
                        FulfillmentStatus.Fulfilled or FulfillmentStatus.FulfilledExternally))
                {
                    eligible.Add(candidateId);
                }
            }

            return eligible;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The download repository failed to list fulfilled downloads for {DownloadId}.",
                importedDownloadId);
            throw;
        }
    }

    private static void RecordTransition(DownloadStatus from, DownloadStatus to, DownloadFailureReason? failureReason)
    {
        DownloadTelemetry.Transitions.Add(1, new("from_status", from.ToString()), new("to_status", to.ToString()));
        if (DownloadStatuses.Failures.Contains(to) && from != to)
        {
            DownloadTelemetry.Failures.Add(
                1,
                new KeyValuePair<string, object?>("reason", failureReason?.ToString() ?? "none"));
        }
    }

    private static List<Guid> ContentItemIdsOf(IReadOnlyCollection<DownloadScopeRow> scope)
    {
        return [.. scope.Select(row => row.ContentItemId).Distinct()];
    }

    private static ContentEventImpact? ImpactOf(IReadOnlyCollection<DownloadScopeRow> scope, Guid downloadId)
    {
        var rows = scope.Where(row => row.DownloadId == downloadId).ToList();
        if (rows.Count == 0)
        {
            return null;
        }

        if (rows.Count > 1)
        {
            return new() { BatchIds = [.. rows.Select(row => row.HierarchyItemId)] };
        }

        var row = rows[0];
        return new()
        {
            HierarchyItemId = row.HierarchyItemId,
            ItemId = row.ContentItemId,
            MetadataContainerId = row.MetadataContainerId,
            ParentId = row.ParentId,
            ContentType = row.ContentType,
            Level = row.Level,
            TopLevel = row.ParentId is null,
            RootFolderId = row.RootFolderId,
            QualityTierListId = row.QualityTierListId,
            Monitoring = row.EffectiveMonitoring != MonitoringMode.Unmonitored,
            Fulfillment = row.Fulfillment.ToString(),
            WishlistId = row.RequestedViaWishlistId
        };
    }

    private static IReadOnlyCollection<string> ChangedFields(DownloadPatch patch)
    {
        var fields = new List<string>();
        if (patch.Status is not null)
        {
            fields.Add("status");
        }

        if (patch.StatusReason is not null)
        {
            fields.Add("statusReason");
        }

        if (patch.OutputPath is not null)
        {
            fields.Add("outputPath");
        }

        if (patch.DateCompleted is not null)
        {
            fields.Add("dateCompleted");
        }

        if (patch.Progress is not null)
        {
            fields.Add("progress");
        }

        if (patch.LastProgressAt is not null)
        {
            fields.Add("lastProgressAt");
        }

        if (patch.FailureReason is not null)
        {
            fields.Add("failureReason");
        }

        if (patch.FailedAt is not null)
        {
            fields.Add("failedAt");
        }

        if (patch.ClientRemovedAt is not null)
        {
            fields.Add("clientRemovedAt");
        }

        if (patch.HeldByPolicy is not null)
        {
            fields.Add("heldByPolicy");
        }

        return fields;
    }

    private static IQueryable<Download> ApplyNativeSort(
        IQueryable<Download> downloads,
        SortParameter? sort,
        string? resolvedProperty)
    {
        if (sort is null)
        {
            return downloads.OrderBy(d => d.Id);
        }

        var descending = sort.Direction != SortDirection.Ascending;

        if (string.Equals(sort.PropertyName, nameof(DownloadQuery.LastChangedAt), StringComparison.OrdinalIgnoreCase))
        {
            return descending
                ? downloads.OrderByDescending(d => d.ModifiedAt ?? d.CreatedAt).ThenBy(d => d.Id)
                : downloads.OrderBy(d => d.ModifiedAt ?? d.CreatedAt).ThenBy(d => d.Id);
        }

        if (resolvedProperty is null)
        {
            return downloads.OrderBy(d => d.Id);
        }

        if (resolvedProperty == nameof(DownloadQueueEntry.DateCompleted))
        {
            return OrderByEndedAt(downloads, descending).ThenBy(d => d.Id);
        }

        return descending
            ? downloads.OrderByDescending(d => EF.Property<object>(d, resolvedProperty)).ThenBy(d => d.Id)
            : downloads.OrderBy(d => EF.Property<object>(d, resolvedProperty)).ThenBy(d => d.Id);
    }

    private static IOrderedQueryable<Download> OrderByEndedAt(IQueryable<Download> downloads, bool descending)
    {
        return descending
            ? downloads.OrderByDescending(d =>
                DownloadStatuses.Failures.Contains(d.Status)
                    ? d.FailedAt ?? d.DateCompleted ?? d.ModifiedAt ?? d.CreatedAt
                    : d.Status == DownloadStatus.Cancelled
                        ? d.ModifiedAt ?? d.DateCompleted ?? d.CreatedAt
                        : d.DateCompleted ?? d.ModifiedAt ?? d.CreatedAt)
            : downloads.OrderBy(d =>
                DownloadStatuses.Failures.Contains(d.Status)
                    ? d.FailedAt ?? d.DateCompleted ?? d.ModifiedAt ?? d.CreatedAt
                    : d.Status == DownloadStatus.Cancelled
                        ? d.ModifiedAt ?? d.DateCompleted ?? d.CreatedAt
                        : d.DateCompleted ?? d.ModifiedAt ?? d.CreatedAt);
    }

    private static IQueryable<DownloadQueueEntry> ApplySyntheticSort(
        IQueryable<DownloadQueueEntry> entries,
        string resolvedProperty,
        bool descending)
    {
        if (resolvedProperty == nameof(DownloadQueueEntry.ContentHierarchyName))
        {
            return descending
                ? entries.OrderByDescending(e => e.ContentHierarchyName ?? e.Title).ThenBy(e => e.Id)
                : entries.OrderBy(e => e.ContentHierarchyName ?? e.Title).ThenBy(e => e.Id);
        }

        return descending
            ? entries.OrderByDescending(e => e.DownloadClientName).ThenBy(e => e.Id)
            : entries.OrderBy(e => e.DownloadClientName).ThenBy(e => e.Id);
    }

    private static void NormalizeReleaseIdentityKeys(Download download)
    {
        var seen = new HashSet<(string Authority, string Value)>();
        var keys = download.ReleaseIdentityKeys.ToList();
        download.ReleaseIdentityKeys.Clear();
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key.Authority) || string.IsNullOrWhiteSpace(key.Value))
            {
                continue;
            }

            key.Authority = key.Authority.Trim();
            key.Value = key.Value.Trim();
            if (!seen.Add((key.Authority, key.Value)))
            {
                continue;
            }

            key.Ordinal = download.ReleaseIdentityKeys.Count;
            download.ReleaseIdentityKeys.Add(key);
        }
    }

    private string? ResolveSortProperty<TEntity>(string? propertyName)
    {
        return context.Model.FindEntityType(typeof(TEntity))!.GetProperties()
            .Where(property => !property.IsShadowProperty() &&
                (property.ClrType.IsValueType || property.ClrType == typeof(string) ||
                    property.ClrType == typeof(Uri)))
            .Select(property => property.Name)
            .FirstOrDefault(name => string.Equals(name, propertyName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IEnumerable<Download>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.Downloads
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to list the downloads.");
            throw;
        }
    }

    public async Task<Download?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Downloads
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to read download {DownloadId}.", id);
            throw;
        }
    }

    public async Task<PagedList<Download>> QueryAsync(
        DownloadQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var downloads = ApplyQueryFilters(context.Downloads.AsNoTracking().AsQueryable(), query);

            var totalCount = await downloads.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortProperty = ResolveSortProperty<Download>(sort?.PropertyName);
            if (sort is not null && string.Equals(
                sort.PropertyName,
                nameof(DownloadQuery.LastChangedAt),
                StringComparison.OrdinalIgnoreCase))
            {
                downloads = sort.Direction == SortDirection.Ascending
                    ? downloads.OrderBy(d => d.ModifiedAt ?? d.CreatedAt).ThenBy(d => d.Id)
                    : downloads.OrderByDescending(d => d.ModifiedAt ?? d.CreatedAt).ThenBy(d => d.Id);
            }
            else if (sort is not null && sortProperty is not null)
            {
                var descending = sort.Direction != SortDirection.Ascending;
                if (sortProperty == nameof(Download.DateCompleted))
                {
                    downloads = OrderByEndedAt(downloads, descending).ThenBy(d => d.Id);
                }
                else
                {
                    downloads = descending
                        ? downloads.OrderByDescending(d => EF.Property<object>(d, sortProperty)).ThenBy(d => d.Id)
                        : downloads.OrderBy(d => EF.Property<object>(d, sortProperty)).ThenBy(d => d.Id);
                }
            }
            else
            {
                downloads = downloads.OrderBy(d => d.Id);
            }

            downloads = downloads.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            var items = await downloads.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = items,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalItems = totalCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to query the downloads.");
            throw;
        }
    }

    public async Task<PagedList<DownloadQueueEntry>> QueryQueueEntriesAsync(
        DownloadQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filtered = ApplyQueryFilters(context.Downloads.AsNoTracking().AsQueryable(), query);

            var totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortProperty = new[]
                {
                    nameof(DownloadQueueEntry.ContentHierarchyName),
                    nameof(DownloadQueueEntry.DownloadClientName)
                }
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase))
                ?? ResolveSortProperty<Download>(sort?.PropertyName);
            var descending = sort is not null && sort.Direction != SortDirection.Ascending;

            var entries = sortProperty
                is nameof(DownloadQueueEntry.ContentHierarchyName) or nameof(DownloadQueueEntry.DownloadClientName)
                ? ApplySyntheticSort(ProjectToQueueEntry(filtered), sortProperty, descending)
                : ProjectToQueueEntry(ApplyNativeSort(filtered, sort, sortProperty));

            entries = entries.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            var items = await entries.ToListAsync(cancellationToken).ConfigureAwait(false);
            items = await ResolvePostersAsync(items, cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = items,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalItems = totalCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to query the download queue entries.");
            throw;
        }
    }

    public async Task<DownloadQueueEntry?> GetQueueEntryByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = await ProjectToQueueEntry(context.Downloads.AsNoTracking().Where(d => d.Id == id))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (entry is null)
            {
                return null;
            }

            var resolved = await ResolvePostersAsync([entry], cancellationToken).ConfigureAwait(false);
            return resolved[0];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to read the download queue entry {DownloadId}.", id);
            throw;
        }
    }

    private IQueryable<DownloadScope> TopmostScopes()
    {
        return
            from scope in context.DownloadScopes.AsNoTracking()
            join node in context.ContentHierarchyItems.AsNoTracking() on scope.ContentHierarchyItemId equals node.Id
            where node.ParentId == null || !context.DownloadScopes.Any(parent =>
                parent.DownloadId == scope.DownloadId && parent.ContentHierarchyItemId == node.ParentId)
            select scope;
    }

    private IQueryable<DownloadQueueEntry> ProjectToQueueEntry(IQueryable<Download> downloads)
    {
        var topmostScopes = TopmostScopes();
        return
            from d in downloads
            join scope in context.DownloadScopes.AsNoTracking() on (
                from candidate in topmostScopes
                where candidate.DownloadId == d.Id
                orderby candidate.Id
                select (Guid?)candidate.Id).FirstOrDefault() equals (Guid?)scope.Id into scopes
            from scope in scopes.DefaultIfEmpty()
            join node in context.ContentHierarchyItems.AsNoTracking()
                on scope.ContentHierarchyItemId equals node.Id into nodes
            from node in nodes.DefaultIfEmpty()
            select new DownloadQueueEntry
            {
                Id = d.Id,
                ReleaseId = d.ReleaseId,
                DownloadClientId = d.DownloadClientId,
                DownloadClientName = (
                    from c in context.DownloadClients
                    where c.Id == d.DownloadClientId
                    select c.Name)
                    .FirstOrDefault(),
                ProviderType = (
                    from c in context.DownloadClients
                    where c.Id == d.DownloadClientId
                    select c.ProviderType)
                    .FirstOrDefault(),
                ExternalId = d.ExternalId,
                Title = d.Title,
                ContentHierarchyName = node == null ? null : node.Name,
                ContentMetadataTitle = node == null
                    ? null
                    : (
                        from contribution in node.MetadataContainer!.Contributions
                        where contribution.Title != null && contribution.Title != ""
                        orderby contribution.Ordinal
                        select contribution.Title).FirstOrDefault(),
                ContentHierarchyItemId = node == null ? null : (Guid?)node.Id,
                ContentHierarchyParentId = node == null ? null : node.ParentId,
                ReleaseInfoUrl = d.ReleaseId == null
                    ? null
                    : (
                        from r in context.Releases
                        where r.Id == d.ReleaseId && r.InfoUrl != null
                        select r.InfoUrl!.ToString())
                        .FirstOrDefault(),
                Status = d.Status,
                OutputPath = d.OutputPath,
                StatusReason = d.StatusReason,
                DateCompleted = d.DateCompleted,
                Progress = d.Progress,
                LastProgressAt = d.LastProgressAt,
                FailedAt = d.FailedAt,
                FailureReason = d.FailureReason,
                CreatedAt = d.CreatedAt,
                ModifiedAt = d.ModifiedAt,
                ClientRemovedAt = d.ClientRemovedAt,
                HeldByPolicy = d.HeldByPolicy
            };
    }

    private async Task<List<DownloadQueueEntry>> ResolvePostersAsync(
        List<DownloadQueueEntry> entries,
        CancellationToken cancellationToken)
    {
        var hierarchyItemIds = (
            from e in entries
            where e.ContentHierarchyItemId is not null
            select e.ContentHierarchyItemId!.Value)
            .Distinct()
            .ToList();
        if (hierarchyItemIds.Count == 0)
        {
            return entries;
        }

        var images = await (
            from hi in context.ContentHierarchyItems.AsNoTracking()
            where hierarchyItemIds.Contains(hi.Id)
            join mc in context.MetadataContributions.AsNoTracking()
                on hi.MetadataContainerId equals mc.MetadataContainerId
            from img in mc.Images
            select new
            {
                HierarchyItemId = hi.Id,
                ContributionOrdinal = mc.Ordinal,
                img.Type,
                img.Ordinal,
                img.Url
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var postersByHierarchyItem = images
            .GroupBy(x => x.HierarchyItemId)
            .ToDictionary(
                g => g.Key,
                g => (
                    from image in g
                    orderby string.Equals(image.Type, "poster", StringComparison.OrdinalIgnoreCase) descending,
                        string.Equals(image.Type, "primary", StringComparison.OrdinalIgnoreCase) descending,
                        string.Equals(image.Type, "profile", StringComparison.OrdinalIgnoreCase) descending,
                        image.ContributionOrdinal,
                        image.Ordinal
                    select image.Url.ToString()).First());

        return [.. entries
            .Select(e => e.ContentHierarchyItemId is not null &&
                postersByHierarchyItem.TryGetValue(e.ContentHierarchyItemId.Value, out var url)
                ? e with { PosterUrl = url }
                : e)];
    }

    private IQueryable<Download> ApplyQueryFilters(IQueryable<Download> downloads, DownloadQuery query)
    {
        var releaseIds = query.ReleaseIds?.ToHashSet() ?? [];
        if (query.ReleaseId is { } releaseId)
        {
            releaseIds.Add(releaseId);
        }

        if (releaseIds.Count > 0)
        {
            downloads = downloads.Where(d => d.ReleaseId != null && releaseIds.Contains(d.ReleaseId.Value));
        }

        var contentHierarchyItemIds = query.ContentHierarchyItemIds?.ToHashSet() ?? [];
        if (query.ContentItemId is { } contentHierarchyItemId)
        {
            contentHierarchyItemIds.Add(contentHierarchyItemId);
        }

        if (contentHierarchyItemIds.Count > 0)
        {
            var downloadIds =
                from c in context.DownloadScopes.AsNoTracking()
                where contentHierarchyItemIds.Contains(c.ContentHierarchyItemId)
                select c.DownloadId;
            downloads = downloads.Where(d => downloadIds.Contains(d.Id));
        }

        if (query.DownloadClientId is not null)
        {
            downloads = downloads.Where(d => d.DownloadClientId == query.DownloadClientId);
        }

        if (query.Status is not null)
        {
            downloads = downloads.Where(d => d.Status == query.Status);
        }

        if (query.Statuses is { Count: > 0 })
        {
            var statuses = query.Statuses as DownloadStatus[] ?? [.. query.Statuses];
            downloads = downloads.Where(d => statuses.Contains(d.Status));
        }

        if (query.ExcludeIds is { Count: > 0 })
        {
            var excludeIds = query.ExcludeIds as Guid[] ?? [.. query.ExcludeIds];
            downloads = downloads.Where(d => !excludeIds.Contains(d.Id));
        }

        if (query.HasImportRecoveryState is { } hasRecovery)
        {
            downloads = downloads.Where(download => (download.ImportRecoveryState != null) == hasRecovery);
        }

        if (query.LastChangedAt?.Min is not null)
        {
            downloads = downloads.Where(d => (d.ModifiedAt ?? d.CreatedAt) >= query.LastChangedAt.Min);
        }

        if (query.LastChangedAt?.Max is not null)
        {
            downloads = downloads.Where(d => (d.ModifiedAt ?? d.CreatedAt) <= query.LastChangedAt.Max);
        }

        if (query.DateCompleted?.Min is not null)
        {
            downloads = downloads.Where(d => d.DateCompleted >= query.DateCompleted.Min);
        }

        if (query.DateCompleted?.Max is not null)
        {
            downloads = downloads.Where(d => d.DateCompleted <= query.DateCompleted.Max);
        }

        return downloads;
    }

    public async Task<Download> CreateAsync(Download model, Guid? createdByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var affectedScope = new List<DownloadScopeRow>();
            var created = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    model.CreatedAt = now;
                    model.CreatedByUserId = createdByUserId;
                    NormalizeReleaseIdentityKeys(model);

                    context.Downloads.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(Download),
                            Detail = $"Created download {model.Title}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    affectedScope = await ListScopedContentAsync([model.Id], cancellationToken)
                        .ConfigureAwait(false);
                    var affectedContentItemIds = ContentItemIdsOf(affectedScope);
                    await RecomputeActiveAcquisitionStatusAsync(
                        affectedContentItemIds,
                        now,
                        createdByUserId,
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    DownloadEventBuilder.BuildDownloadCreatedEvent(
                        created.Id,
                        created.ReleaseId,
                        ImpactOf(affectedScope, created.Id)),
                    token),
                cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to create download {DownloadTitle}.", model.Title);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(Download model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var affectedScope = new List<DownloadScopeRow>();
            var wasUpdated = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    NormalizeReleaseIdentityKeys(model);

                    var updated = await context.Downloads
                        .Where(d => d.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(d => d.ReleaseId, model.ReleaseId)
                                .SetProperty(d => d.ReleaseSourceId, model.ReleaseSourceId)
                                .SetProperty(d => d.DownloadClientId, model.DownloadClientId)
                                .SetProperty(d => d.ExternalId, model.ExternalId)
                                .SetProperty(d => d.Title, model.Title)
                                .SetProperty(d => d.Status, model.Status)
                                .SetProperty(d => d.OutputPath, model.OutputPath)
                                .SetProperty(d => d.StatusReason, model.StatusReason)
                                .SetProperty(d => d.DateCompleted, model.DateCompleted)
                                .SetProperty(d => d.HeldByPolicy, model.HeldByPolicy)
                                .SetProperty(d => d.Progress, model.Progress)
                                .SetProperty(d => d.LastProgressAt, model.LastProgressAt)
                                .SetProperty(d => d.FailureReason, model.FailureReason)
                                .SetProperty(d => d.FailedAt, model.FailedAt)
                                .SetProperty(d => d.ClientRemovedAt, model.ClientRemovedAt)
                                .SetProperty(d => d.ModifiedAt, now)
                                .SetProperty(d => d.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (updated > 0)
                    {
                        await ReplaceDownloadReleaseIdentityKeysAsync(model, cancellationToken).ConfigureAwait(false);
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Integrations,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(Download),
                                Detail = $"Updated download {model.Title}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);

                        affectedScope = await ListScopedContentAsync([model.Id], cancellationToken)
                            .ConfigureAwait(false);
                        var affectedContentItemIds = ContentItemIdsOf(affectedScope);
                        await RecomputeActiveAcquisitionStatusAsync(
                            affectedContentItemIds,
                            now,
                            modifiedByUserId,
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (wasUpdated)
            {
                await PublishUpdatedAsync(model.Id, FullUpdateFields, affectedScope, cancellationToken)
                    .ConfigureAwait(false);
            }

            return wasUpdated;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to update download {DownloadId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        DownloadPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        var result = await BulkPatchAsync(
            new() { Ids = [patch.Id], Patch = patch },
            modifiedByUserId,
            cancellationToken).ConfigureAwait(false);
        return result.Count > 0;
    }

    /// <inheritdoc />
    public async Task LockAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            await context.Downloads
                .Where(download => download.Id == id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(download => download.ModifiedAt, download => download.ModifiedAt),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to lock download {DownloadId}.", id);
            throw;
        }
    }

    public async Task<BulkResult> BulkPatchAsync(
        BulkPatch<DownloadQuery, DownloadPatch> patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var ids = await ResolveBulkTargetIdsAsync(patch, ct).ConfigureAwait(false);
                    var downloads = await context.Downloads.AsNoTracking()
                        .Where(d => ids.Contains(d.Id))
                        .Select(d => new { d.Id, d.Title })
                        .ToListAsync(ct).ConfigureAwait(false);
                    if (downloads.Count == 0)
                    {
                        return new BulkResult { Count = 0 };
                    }

                    var targets = downloads.Select(d => d.Id).ToList();
                    var now = _time.GetUtcNow().UtcDateTime;
                    var affectedScope = await ListScopedContentAsync(targets, ct).ConfigureAwait(false);
                    var affected = await context.Downloads.Where(d => targets.Contains(d.Id))
                        .ExecuteUpdateAsync(
                            setters =>
                            {
                                if (patch.Patch.Status is not null)
                                {
                                    setters.SetProperty(d => d.Status, patch.Patch.Status.Value);
                                }

                                if (patch.Patch.StatusReason is not null)
                                {
                                    setters.SetProperty(d => d.StatusReason, patch.Patch.StatusReason.Value);
                                }

                                if (patch.Patch.OutputPath is not null)
                                {
                                    setters.SetProperty(d => d.OutputPath, patch.Patch.OutputPath.Value);
                                }

                                if (patch.Patch.DateCompleted is not null)
                                {
                                    setters.SetProperty(d => d.DateCompleted, patch.Patch.DateCompleted.Value);
                                }

                                if (patch.Patch.Progress is not null)
                                {
                                    setters.SetProperty(d => d.Progress, patch.Patch.Progress.Value);
                                }

                                if (patch.Patch.LastProgressAt is not null)
                                {
                                    setters.SetProperty(d => d.LastProgressAt, patch.Patch.LastProgressAt.Value);
                                }

                                if (patch.Patch.FailureReason is not null)
                                {
                                    setters.SetProperty(d => d.FailureReason, patch.Patch.FailureReason.Value);
                                }

                                if (patch.Patch.FailedAt is not null)
                                {
                                    setters.SetProperty(d => d.FailedAt, patch.Patch.FailedAt.Value);
                                }

                                if (patch.Patch.ClientRemovedAt is not null)
                                {
                                    setters.SetProperty(d => d.ClientRemovedAt, patch.Patch.ClientRemovedAt.Value);
                                }

                                if (patch.Patch.HeldByPolicy is not null)
                                {
                                    setters.SetProperty(d => d.HeldByPolicy, patch.Patch.HeldByPolicy.Value);
                                }

                                setters.SetProperty(d => d.ModifiedAt, now);
                                setters.SetProperty(d => d.ModifiedByUserId, modifiedByUserId);
                            },
                            ct).ConfigureAwait(false);
                    if (affected > 0)
                    {
                        foreach (var download in downloads)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.Integrations,
                                    Operation = "Patch",
                                    EntityId = download.Id.ToString(),
                                    EntityType = nameof(Download),
                                    Detail = $"Updated download {download.Title}.",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                },
                                ct).ConfigureAwait(false);
                        }

                        await RecomputeActiveAcquisitionStatusAsync(
                            ContentItemIdsOf(affectedScope),
                            now,
                            modifiedByUserId,
                            ct).ConfigureAwait(false);
                        foreach (var id in targets)
                        {
                            await PublishUpdatedAsync(id, ChangedFields(patch.Patch), affectedScope, ct)
                                .ConfigureAwait(false);
                        }
                    }

                    return new BulkResult { Count = affected };
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to patch the downloads.");
            throw;
        }
    }

    public async Task<BulkResult> BulkDeleteAsync(
        BulkAction<DownloadQuery> action,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var affectedScope = new List<DownloadScopeRow>();
            var deletedIds = new List<Guid>();
            var result = await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var ids = await ResolveBulkTargetIdsAsync(action, cancellationToken).ConfigureAwait(false);
                    if (ids.Count == 0)
                    {
                        return new BulkResult { Count = 0 };
                    }

                    affectedScope = await ListScopedContentAsync(ids, ct).ConfigureAwait(false);

                    var affectedContentItemIds = ContentItemIdsOf(affectedScope);

                    deletedIds = await context.Downloads
                        .AsNoTracking()
                        .Where(d => ids.Contains(d.Id))
                        .Select(d => d.Id)
                        .ToListAsync(ct)
                        .ConfigureAwait(false);

                    var affected = await context.Downloads
                        .Where(d => deletedIds.Contains(d.Id))
                        .ExecuteDeleteAsync(ct)
                        .ConfigureAwait(false);

                    var downloadCountLabel = affected == 1 ? "1 download" : $"{affected} downloads";
                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "Delete",
                            EntityType = nameof(Download),
                            Detail = $"Deleted {downloadCountLabel}.",
                            Timestamp = now,
                            UserId = deletedByUserId
                        },
                        ct).ConfigureAwait(false);

                    await RecomputeActiveAcquisitionStatusAsync(affectedContentItemIds, now, deletedByUserId, ct)
                        .ConfigureAwait(false);

                    return new BulkResult { Count = affected };
                },
                cancellationToken).ConfigureAwait(false);

            await PublishDeletedAsync(deletedIds, affectedScope, cancellationToken).ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to bulk-delete the downloads.");
            throw;
        }
    }

    private async Task<List<Guid>> ResolveBulkTargetIdsAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>();
        if (action.Ids is { Count: > 0 })
        {
            foreach (var id in action.Ids)
            {
                ids.Add(id);
            }
        }

        if (action.Filter is not null)
        {
            var filteredIds = await ApplyQueryFilters(context.Downloads.AsNoTracking().AsQueryable(), action.Filter)
                .Select(d => d.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var id in filteredIds)
            {
                ids.Add(id);
            }
        }

        return [.. ids];
    }

    public async Task<IReadOnlyList<Download>> ListByReleaseAsync(
        IReadOnlyCollection<Release> releases,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (releases.Count == 0)
            {
                return [];
            }

            var releaseIds = releases.Select(release => release.Id).Distinct().ToList();
            var sourceSet = releases
                .Where(release => release.IndexerId is not null && !string.IsNullOrWhiteSpace(release.ExternalId))
                .Select(release => (IndexerId: release.IndexerId!.Value, ExternalId: release.ExternalId!))
                .ToHashSet();
            var keySet = releases
                .SelectMany(release => release.IdentityKeys)
                .Where(k => !string.IsNullOrWhiteSpace(k.Authority) && !string.IsNullOrWhiteSpace(k.Value))
                .Select(k => (Authority: k.Authority.Trim(), Value: k.Value.Trim()))
                .ToHashSet();

            var indexerIds = sourceSet.Select(source => source.IndexerId).Distinct().ToList();
            var externalIds = sourceSet.Select(source => source.ExternalId).Distinct().ToList();
            var authorities = keySet.Select(k => k.Authority).Distinct().ToList();
            var values = keySet.Select(k => k.Value).Distinct().ToList();
            var downloads = await context.Downloads
                .AsNoTracking()
                .Include(download => download.Release)
                .Include(download => download.ReleaseIdentityKeys)
                .Include(download => download.Scope)
                .Where(download =>
                    (download.ReleaseId != null && releaseIds.Contains(download.ReleaseId.Value)) ||
                    (download.Release != null && download.Release.IndexerId != null &&
                        indexerIds.Contains(download.Release.IndexerId.Value) &&
                        externalIds.Contains(download.Release.ExternalId!)) ||
                    download.ReleaseIdentityKeys.Any(identity =>
                        authorities.Contains(identity.Authority) && values.Contains(identity.Value)))
                .Where(download => download.Status != DownloadStatus.Failed ||
                    download.FailureReason != DownloadFailureReason.ClientRejected ||
                    !context.ReleaseSources.Any(source =>
                        source.ReleaseId == download.ReleaseId &&
                        source.IndexerConnectionId != null &&
                        source.CreatedAt > download.FailedAt))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return
            [
                .. downloads.Where(download =>
                    (download.ReleaseId is { } releaseId && releaseIds.Contains(releaseId)) ||
                    (download.Release is { IndexerId: { } indexerId, ExternalId: { } externalId } &&
                        sourceSet.Contains((indexerId, externalId))) ||
                    download.ReleaseIdentityKeys.Any(identity => keySet.Contains((identity.Authority, identity.Value))))
            ];
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The download repository failed to list the downloads for {ReleaseCount} releases.",
                releases.Count);
            throw;
        }
    }

    private async Task ReplaceDownloadReleaseIdentityKeysAsync(Download download, CancellationToken cancellationToken)
    {
        await context.ReleaseIdentityKeys
            .Where(k => k.DownloadId == download.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var key in download.ReleaseIdentityKeys
            .Where(k => !string.IsNullOrWhiteSpace(k.Authority) && !string.IsNullOrWhiteSpace(k.Value))
            .DistinctBy(k => (k.Authority.Trim(), k.Value.Trim())))
        {
            context.ReleaseIdentityKeys.Add(new ReleaseIdentityKey
            {
                DownloadId = download.Id,
                Authority = key.Authority.Trim(),
                Value = key.Value.Trim(),
                Ordinal = key.Ordinal
            });
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<DownloadScopeRow>> ListScopedContentAsync(
        IReadOnlyCollection<Guid> downloadIds,
        CancellationToken cancellationToken)
    {
        if (downloadIds.Count == 0)
        {
            return [];
        }

        return await (
            from scope in context.DownloadScopes.AsNoTracking()
            where downloadIds.Contains(scope.DownloadId)
            join ci in context.ContentItems.AsNoTracking()
                on scope.ContentHierarchyItemId equals ci.ContentHierarchyItemId
            join item in context.ContentHierarchyItems.AsNoTracking()
                on scope.ContentHierarchyItemId equals item.Id
            select new DownloadScopeRow(
                scope.DownloadId,
                item.Id,
                ci.Id,
                item.MetadataContainerId,
                item.ParentId,
                item.ContentType,
                item.Level,
                item.RootFolderId,
                item.QualityTierListId,
                item.EffectiveMonitoring,
                ci.Fulfillment,
                ci.RequestedViaWishlistId))
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<bool> UpdateRefreshedStatusAsync(
        Guid downloadId,
        DownloadStatus status,
        string? statusReason,
        string? outputPath,
        DateTime? dateCompleted,
        decimal? progress,
        DateTime? lastProgressAt,
        bool heldByPolicy,
        DownloadFailureReason? failureReason,
        DateTime? failedAt,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        return UpdateRefreshedStatusAsync(
            d => d.Status != DownloadStatus.Imported && d.Status != DownloadStatus.Importing &&
                d.Status != DownloadStatus.Cancelled && d.Status != DownloadStatus.Failed &&
                d.Status != DownloadStatus.Faulted,
            downloadId,
            status,
            statusReason,
            outputPath,
            dateCompleted,
            progress,
            lastProgressAt,
            heldByPolicy,
            failureReason,
            failedAt,
            now,
            userId,
            cancellationToken);
    }

    public Task<bool> UpdateActiveRefreshedStatusAsync(
        Guid downloadId,
        DownloadStatus status,
        string? statusReason,
        string? outputPath,
        DateTime? dateCompleted,
        decimal? progress,
        DateTime? lastProgressAt,
        bool heldByPolicy,
        DownloadFailureReason? failureReason,
        DateTime? failedAt,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        return UpdateRefreshedStatusAsync(
            d => d.Status == DownloadStatus.Downloading || d.Status == DownloadStatus.Stalled ||
                d.Status == DownloadStatus.Grabbed,
            downloadId,
            status,
            statusReason,
            outputPath,
            dateCompleted,
            progress,
            lastProgressAt,
            heldByPolicy,
            failureReason,
            failedAt,
            now,
            userId,
            cancellationToken);
    }

    private async Task RecomputeActiveAcquisitionStatusAsync(
        IReadOnlyCollection<Guid> contentItemIds,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var changes = await ListAcquisitionStatusChangesAsync(contentItemIds, cancellationToken)
            .ConfigureAwait(false);
        foreach (var change in changes)
        {
            await SetActiveAcquisitionStatusAsync(change, now, userId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<AcquisitionStatusChange>> ListAcquisitionStatusChangesAsync(
        IReadOnlyCollection<Guid> contentItemIds,
        CancellationToken cancellationToken)
    {
        if (contentItemIds.Count == 0)
        {
            return [];
        }

        var distinctIds = contentItemIds.Distinct().ToList();

        var computed = await context.ContentItems.AsNoTracking()
            .Where(ci => distinctIds.Contains(ci.Id))
            .Select(ci => new
            {
                ci.Id,
                Current = ci.ActiveAcquisitionStatus,
                Next = (
                    from scope in context.DownloadScopes
                    join d in context.Downloads on scope.DownloadId equals d.Id
                    where scope.ContentHierarchyItemId == ci.ContentHierarchyItemId &&
                        !DownloadStatuses.Finished.Contains(d.Status)
                    orderby d.ModifiedAt ?? d.CreatedAt descending
                    select (DownloadStatus?)d.Status).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. computed.Where(x => x.Current != x.Next).Select(x => new AcquisitionStatusChange(x.Id, x.Next))];
    }

    private Task<int> SetActiveAcquisitionStatusAsync(
        AcquisitionStatusChange change,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        return context.ContentItems
            .Where(ci => ci.Id == change.ContentItemId && ci.ActiveAcquisitionStatus != change.Next)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(ci => ci.ActiveAcquisitionStatus, change.Next)
                    .SetProperty(ci => ci.ModifiedAt, now)
                    .SetProperty(ci => ci.ModifiedByUserId, userId),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateRefreshedClientStateAsync(
        Guid downloadId,
        DownloadStatus status,
        string? statusReason,
        string? outputPath,
        DateTime? dateCompleted,
        decimal? progress,
        DateTime? lastProgressAt,
        bool heldByPolicy,
        DownloadFailureReason? failureReason,
        DateTime? failedAt,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var affected = await ExecuteRefreshedUpdateAsync(
                context.Downloads.Where(d => d.Id == downloadId && d.Status == status &&
                    d.Status != DownloadStatus.Imported && d.Status != DownloadStatus.Importing &&
                    d.Status != DownloadStatus.Cancelled && d.Status != DownloadStatus.Failed &&
                    d.Status != DownloadStatus.Faulted),
                status,
                statusReason,
                outputPath,
                dateCompleted,
                progress,
                lastProgressAt,
                heldByPolicy,
                failureReason,
                failedAt,
                now,
                userId,
                cancellationToken).ConfigureAwait(false);
            if (affected == 0)
            {
                return false;
            }

            RecordTransition(status, status, failureReason);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The download repository failed to update the refreshed client state of download {DownloadId}.",
                downloadId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task PublishRefreshedStatusAsync(
        IReadOnlyCollection<Guid> downloadIds,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (downloadIds.Count == 0)
        {
            return;
        }

        var failures = new List<Exception>();
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var scope = await ListScopedContentAsync(downloadIds, cancellationToken).ConfigureAwait(false);
            var changes = await ListAcquisitionStatusChangesAsync(ContentItemIdsOf(scope), cancellationToken)
                .ConfigureAwait(false);

            foreach (var change in changes)
            {
                try
                {
                    await SetActiveAcquisitionStatusAsync(change, now, userId, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(
                        ex,
                        "The download repository failed to set the acquisition status of content item {ContentItemId}.",
                        change.ContentItemId);
                    failures.Add(ex);
                }
            }

            foreach (var downloadId in downloadIds)
            {
                try
                {
                    await PublishUpdatedAsync(downloadId, RefreshedFields, scope, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(
                        ex,
                        "The download repository failed to publish the refreshed status of download {DownloadId}.",
                        downloadId);
                    failures.Add(ex);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The download repository failed to publish the refreshed status of {DownloadCount} downloads.",
                downloadIds.Count);
            throw;
        }

        if (failures.Count > 0)
        {
            throw new AggregateException(failures);
        }
    }

    private Task<int> ExecuteRefreshedUpdateAsync(
        IQueryable<Download> downloads,
        DownloadStatus status,
        string? statusReason,
        string? outputPath,
        DateTime? dateCompleted,
        decimal? progress,
        DateTime? lastProgressAt,
        bool heldByPolicy,
        DownloadFailureReason? failureReason,
        DateTime? failedAt,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        return downloads.ExecuteUpdateAsync(
            s => s
                .SetProperty(d => d.Status, status)
                .SetProperty(d => d.StatusReason, statusReason)
                .SetProperty(d => d.OutputPath, outputPath)
                .SetProperty(d => d.DateCompleted, dateCompleted)
                .SetProperty(d => d.Progress, progress)
                .SetProperty(d => d.LastProgressAt, lastProgressAt)
                .SetProperty(d => d.HeldByPolicy, heldByPolicy)
                .SetProperty(d => d.FailureReason, failureReason)
                .SetProperty(d => d.FailedAt, failedAt)
                .SetProperty(d => d.ModifiedAt, now)
                .SetProperty(d => d.ModifiedByUserId, userId),
            cancellationToken);
    }

    private async Task<bool> UpdateRefreshedStatusAsync(
        Expression<Func<Download, bool>> guard,
        Guid downloadId,
        DownloadStatus status,
        string? statusReason,
        string? outputPath,
        DateTime? dateCompleted,
        decimal? progress,
        DateTime? lastProgressAt,
        bool heldByPolicy,
        DownloadFailureReason? failureReason,
        DateTime? failedAt,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var affectedScope = new List<DownloadScopeRow>();
            var affected = 0;
            DownloadStatus? priorStatus = null;
            Guid? releaseId = null;
            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var prior = await context.Downloads
                        .AsNoTracking()
                        .Where(d => d.Id == downloadId)
                        .Select(d => new { d.Status, d.ReleaseId })
                        .FirstOrDefaultAsync(ct)
                        .ConfigureAwait(false);
                    if (prior is null)
                    {
                        return;
                    }

                    priorStatus = prior.Status;
                    releaseId = prior.ReleaseId;
                    affected = await ExecuteRefreshedUpdateAsync(
                        context.Downloads.Where(guard).Where(d => d.Id == downloadId),
                        status,
                        statusReason,
                        outputPath,
                        dateCompleted,
                        progress,
                        lastProgressAt,
                        heldByPolicy,
                        failureReason,
                        failedAt,
                        now,
                        userId,
                        ct).ConfigureAwait(false);

                    if (affected > 0)
                    {
                        affectedScope = await ListScopedContentAsync([downloadId], ct)
                            .ConfigureAwait(false);
                        var affectedContentItemIds = ContentItemIdsOf(affectedScope);
                        await RecomputeActiveAcquisitionStatusAsync(affectedContentItemIds, now, userId, ct)
                            .ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);

            if (affected > 0 && priorStatus is { } from)
            {
                RecordTransition(from, status, failureReason);

                await transactionManager.OnCommittedAsync(
                    token => PublishStatusTransitionEventsAsync(
                        downloadId,
                        releaseId,
                        from,
                        status,
                        statusReason,
                        failureReason,
                        affectedScope,
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return affected > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The download repository failed to update the refreshed status of download {DownloadId}.",
                downloadId);
            throw;
        }
    }

    public async Task UpdateImportedHoldTrackingAsync(
        Guid downloadId,
        bool heldByPolicy,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var affected = await context.Downloads
                .Where(d => d.Id == downloadId && d.Status == DownloadStatus.Imported)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(d => d.HeldByPolicy, heldByPolicy)
                        .SetProperty(d => d.ModifiedAt, now)
                        .SetProperty(d => d.ModifiedByUserId, userId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (affected > 0)
            {
                await PublishUpdatedAsync(downloadId, ["heldByPolicy"], [], cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The download repository failed to update the imported hold tracking for download {DownloadId}.",
                downloadId);
            throw;
        }
    }

    public Task<bool> MarkImportingAsync(
        Guid downloadId,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken = default)
        => MarkImportingAsync(downloadId, now, userId, null, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> MarkImportingAsync(
        Guid downloadId,
        DateTime now,
        Guid? userId,
        ImportCollisionMode? collisionMode,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var affectedScope = new List<DownloadScopeRow>();
                    var affected = await context.Downloads
                        .Where(d => d.Id == downloadId && d.Status == DownloadStatus.Downloaded)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(d => d.Status, DownloadStatus.Importing)
                                .SetProperty(d => d.ImportCollisionMode, collisionMode)
                                .SetProperty(d => d.ModifiedAt, now)
                                .SetProperty(d => d.ModifiedByUserId, userId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (affected > 0)
                    {
                        affectedScope = await ListScopedContentAsync([downloadId], cancellationToken)
                            .ConfigureAwait(false);
                        var affectedContentItemIds = ContentItemIdsOf(affectedScope);
                        await RecomputeActiveAcquisitionStatusAsync(
                            affectedContentItemIds,
                            now,
                            userId,
                            cancellationToken)
                            .ConfigureAwait(false);
                        await PublishUpdatedAsync(downloadId, ["status"], affectedScope, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    return affected > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The download repository failed to mark download {DownloadId} as importing.",
                downloadId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetImportCollisionModeAsync(
        Guid downloadId,
        ImportCollisionMode collisionMode,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.Downloads.Where(download => download.Id == downloadId &&
                    download.Status == DownloadStatus.Importing)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(download => download.ImportCollisionMode, collisionMode),
                    cancellationToken)
                .ConfigureAwait(false) > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The download repository failed to store the import policy for {DownloadId}.",
                downloadId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SetImportCapacityReasonAsync(
        Guid downloadId,
        string? reason,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        try
        {
            await transactionManager.ExecuteWithTransactionAsync(async ct =>
            {
                var affected = await context.Downloads
                    .Where(download => download.Id == downloadId && download.Status == DownloadStatus.Importing &&
                        download.StatusReason != reason)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(download => download.StatusReason, reason)
                        .SetProperty(download => download.ModifiedAt, now)
                        .SetProperty(download => download.ModifiedByUserId, userId),
                        ct).ConfigureAwait(false);
                if (affected > 0)
                {
                    await auditRecordRepository.CreateAsync(new AuditRecord
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "ImportCapacityChecked",
                        EntityType = nameof(Download),
                        EntityId = downloadId.ToString(),
                        Detail = reason ?? "Destination capacity is sufficient. The import can continue.",
                        Timestamp = now,
                        UserId = userId
                    }, ct).ConfigureAwait(false);
                    await PublishUpdatedAsync(downloadId, ["statusReason"], [], ct).ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The download repository failed to store the capacity reason for {DownloadId}.",
                downloadId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SetImportRecoveryStateAsync(
        Guid downloadId,
        string? state,
        string? reason,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        try
        {
            await transactionManager.ExecuteWithTransactionAsync(async ct =>
            {
                var affected = await context.Downloads
                    .Where(download => download.Id == downloadId && (download.Status == DownloadStatus.Importing ||
                        download.Status == DownloadStatus.Imported &&
                        (state == null || download.ImportRecoveryState != null)))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(download => download.ImportRecoveryState, state)
                        .SetProperty(download => download.StatusReason, reason)
                        .SetProperty(download => download.ModifiedAt, now)
                        .SetProperty(download => download.ModifiedByUserId, userId),
                        ct).ConfigureAwait(false);
                if (affected != 1)
                {
                    throw new InvalidOperationException("The download status changed before recovery could be stored.");
                }

                await auditRecordRepository.CreateAsync(new AuditRecord
                {
                    Category = AuditCategories.Integrations,
                    Operation = "ImportRecovery",
                    EntityType = nameof(Download),
                    EntityId = downloadId.ToString(),
                    Detail = reason ?? "Import file recovery completed.",
                    Timestamp = now,
                    UserId = userId
                }, ct).ConfigureAwait(false);
                await PublishUpdatedAsync(downloadId, ["statusReason"], [], ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The download repository failed to store import recovery for {DownloadId}.",
                downloadId);
            throw;
        }
    }

    public async Task MarkImportedAsync(
        Guid downloadId,
        Guid? contentHierarchyItemId,
        int importedFileCount,
        DateTime dateCompleted,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var affectedScope = new List<DownloadScopeRow>();
                    var prior = await (
                        from d in context.Downloads.AsNoTracking()
                        where d.Id == downloadId
                        select new { d.ReleaseId, IndexerId = d.Release != null ? d.Release.IndexerId : null })
                        .FirstOrDefaultAsync(ct)
                        .ConfigureAwait(false);

                    var affected = await context.Downloads
                        .Where(d => d.Id == downloadId)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(d => d.Status, DownloadStatus.Imported)
                                .SetProperty(d => d.StatusReason, (string?)null)
                                .SetProperty(d => d.DateCompleted, dateCompleted)
                                .SetProperty(d => d.ModifiedAt, now)
                                .SetProperty(d => d.ModifiedByUserId, userId),
                            ct)
                        .ConfigureAwait(false);

                    if (affected > 0)
                    {
                        affectedScope = await ListScopedContentAsync([downloadId], ct)
                            .ConfigureAwait(false);
                        var affectedContentItemIds = ContentItemIdsOf(affectedScope);
                        await RecomputeActiveAcquisitionStatusAsync(affectedContentItemIds, now, userId, ct)
                            .ConfigureAwait(false);
                    }

                    if (affected > 0 && prior is not null)
                    {
                        await transactionManager.OnCommittedAsync(
                            token => eventBus.PublishAsync(
                                DownloadEventBuilder.BuildDownloadImportedEvent(
                                    downloadId,
                                    prior.ReleaseId,
                                    ImpactOf(affectedScope, downloadId),
                                    prior.IndexerId,
                                    importedFileCount),
                                token),
                            ct).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The download repository failed to mark download {DownloadId} as imported.",
                downloadId);
            throw;
        }
    }

    public async Task<bool> MarkStuckAsync(
        Guid downloadId,
        string title,
        DateTime inactivityAnchor,
        TimeSpan threshold,
        DateTime now,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var affectedScope = new List<DownloadScopeRow>();
            var transitioned = false;
            Guid? releaseId = null;
            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    releaseId = await context.Downloads
                        .AsNoTracking()
                        .Where(d => d.Id == downloadId)
                        .Select(d => d.ReleaseId)
                        .FirstOrDefaultAsync(ct)
                        .ConfigureAwait(false);
                    var updated = await context.Downloads
                        .Where(d => d.Id == downloadId &&
                            (d.Status == DownloadStatus.Downloading ||
                                d.Status == DownloadStatus.Stalled ||
                                d.Status == DownloadStatus.Grabbed))
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(d => d.Status, DownloadStatus.Faulted)
                                .SetProperty(d => d.FailureReason, DownloadFailureReason.DownloadStuck)
                                .SetProperty(d => d.FailedAt, now)
                                .SetProperty(
                                    d => d.StatusReason,
                                    $"No progress reported since {inactivityAnchor:O} (threshold {threshold}).")
                                .SetProperty(d => d.ModifiedAt, now)
                                .SetProperty(d => d.ModifiedByUserId, userId),
                            ct)
                        .ConfigureAwait(false);

                    if (updated == 0)
                    {
                        return;
                    }

                    transitioned = true;
                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "DownloadStuck",
                            EntityId = downloadId.ToString(),
                            EntityType = nameof(Download),
                            Detail = $"Marked {title} as faulted after no progress since {inactivityAnchor:O}.",
                            Timestamp = now,
                            UserId = userId,
                            CorrelationId = correlationId
                        },
                        ct).ConfigureAwait(false);

                    affectedScope = await ListScopedContentAsync([downloadId], ct)
                        .ConfigureAwait(false);
                    var affectedContentItemIds = ContentItemIdsOf(affectedScope);
                    await RecomputeActiveAcquisitionStatusAsync(affectedContentItemIds, now, userId, ct)
                        .ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            if (transitioned)
            {
                var stuckReasonTag = new KeyValuePair<string, object?>(
                    "reason",
                    nameof(DownloadFailureReason.DownloadStuck));
                DownloadTelemetry.Failures.Add(1, stuckReasonTag);
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        DownloadEventBuilder.BuildDownloadFailedEvent(
                            downloadId,
                            releaseId,
                            DownloadFailureReason.DownloadStuck,
                            ImpactOf(affectedScope, downloadId)),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return transitioned;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to mark download {DownloadId} as stuck.", downloadId);
            throw;
        }
    }

    public async Task<bool> MarkFailedAsync(
        Guid downloadId,
        DownloadStatus status,
        DownloadFailureReason reason,
        string statusReason,
        string auditOperation,
        string auditDetail,
        DateTime now,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!DownloadStatuses.Failures.Contains(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The status is not Failed or Faulted.");
        }

        try
        {
            var affectedScope = new List<DownloadScopeRow>();
            var transitioned = false;
            Guid? releaseId = null;
            Guid? indexerId = null;
            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var prior = await (
                        from d in context.Downloads.AsNoTracking()
                        where d.Id == downloadId
                        select new { d.ReleaseId, IndexerId = d.Release != null ? d.Release.IndexerId : null })
                        .FirstOrDefaultAsync(ct)
                        .ConfigureAwait(false);
                    if (prior is null)
                    {
                        return;
                    }

                    releaseId = prior.ReleaseId;
                    indexerId = prior.IndexerId;
                    var updated = await context.Downloads
                        .Where(d => d.Id == downloadId &&
                            d.Status != DownloadStatus.Cancelled &&
                            d.Status != DownloadStatus.Imported &&
                            !(status == DownloadStatus.Faulted && d.Status == DownloadStatus.Failed))
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(d => d.Status, status)
                                .SetProperty(d => d.FailureReason, (DownloadFailureReason?)reason)
                                .SetProperty(d => d.FailedAt, (DateTime?)now)
                                .SetProperty(d => d.StatusReason, statusReason)
                                .SetProperty(d => d.ModifiedAt, (DateTime?)now)
                                .SetProperty(d => d.ModifiedByUserId, userId),
                            ct)
                        .ConfigureAwait(false);

                    if (updated == 0)
                    {
                        return;
                    }

                    transitioned = true;
                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = auditOperation,
                            EntityId = downloadId.ToString(),
                            EntityType = nameof(Download),
                            Detail = auditDetail,
                            Timestamp = now,
                            UserId = userId,
                            CorrelationId = correlationId
                        },
                        ct).ConfigureAwait(false);

                    affectedScope = await ListScopedContentAsync([downloadId], ct)
                        .ConfigureAwait(false);
                    var affectedContentItemIds = ContentItemIdsOf(affectedScope);
                    await RecomputeActiveAcquisitionStatusAsync(affectedContentItemIds, now, userId, ct)
                        .ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            if (transitioned)
            {
                var failedReasonTag = new KeyValuePair<string, object?>("reason", reason.ToString());
                DownloadTelemetry.Failures.Add(1, failedReasonTag);
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        DownloadEventBuilder.BuildDownloadFailedEvent(
                            downloadId,
                            releaseId,
                            reason,
                            ImpactOf(affectedScope, downloadId),
                            indexerId),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return transitioned;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to mark download {DownloadId} as failed.", downloadId);
            throw;
        }
    }

    public async Task<bool> StampClientRemovedAsync(
        Guid downloadId,
        string title,
        string clientName,
        string trigger,
        DateTime now,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stamped = false;
            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var updated = await context.Downloads
                        .Where(d => d.Id == downloadId && d.ClientRemovedAt == null)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(d => d.ClientRemovedAt, now)
                                .SetProperty(d => d.ModifiedAt, now)
                                .SetProperty(d => d.ModifiedByUserId, userId),
                            ct)
                        .ConfigureAwait(false);

                    if (updated == 0)
                    {
                        return;
                    }

                    stamped = true;
                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "DownloadCleanup",
                            EntityId = downloadId.ToString(),
                            EntityType = nameof(Download),
                            Detail = $"Removed {title} from {clientName} during cleanup because {trigger}.",
                            Timestamp = now,
                            UserId = userId,
                            CorrelationId = correlationId
                        },
                        ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            if (stamped)
            {
                await PublishUpdatedAsync(downloadId, ["clientRemovedAt"], [], cancellationToken)
                    .ConfigureAwait(false);
            }

            return stamped;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The download repository failed to mark download {DownloadId} as removed from its download client.",
                downloadId);
            throw;
        }
    }

    public async Task<IReadOnlyList<StuckDownloadCandidate>> ListStuckCandidatesAsync(
        DateTime fetchingCutoff,
        DateTime stalledCutoff,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var candidates = await (
                from d in context.Downloads.AsNoTracking()
                join c in context.DownloadClients.AsNoTracking() on d.DownloadClientId equals c.Id
                where c.Status != IntegrationHealthStatus.Unhealthy &&
                    c.Status != IntegrationHealthStatus.Stale &&
                    (((d.Status == DownloadStatus.Downloading || d.Status == DownloadStatus.Grabbed) &&
                        (d.LastProgressAt ?? d.CreatedAt) < fetchingCutoff) ||
                        (d.Status == DownloadStatus.Stalled &&
                            (d.LastProgressAt ?? d.CreatedAt) < stalledCutoff))
                select new
                {
                    DownloadId = d.Id,
                    d.Title,
                    d.ExternalId,
                    d.Status,
                    d.Progress,
                    d.LastProgressAt,
                    InactivityAnchor = d.LastProgressAt ?? d.CreatedAt,
                    d.DateCompleted,
                    d.FailureReason,
                    d.FailedAt,
                    d.ReleaseId,
                    DownloadClientName = c.Name,
                    DownloadClientProviderType = c.ProviderType,
                    DownloadClientSettings = c.Settings
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var results = new List<StuckDownloadCandidate>();
            foreach (var c in candidates)
            {
                results.Add(new StuckDownloadCandidate
                {
                    DownloadId = c.DownloadId,
                    ExternalId = c.ExternalId,
                    Title = c.Title,
                    Status = c.Status,
                    Progress = c.Progress,
                    LastProgressAt = c.LastProgressAt,
                    InactivityAnchor = c.InactivityAnchor,
                    DateCompleted = c.DateCompleted,
                    FailureReason = c.FailureReason,
                    FailedAt = c.FailedAt,
                    ReleaseId = c.ReleaseId,
                    ProviderType = c.DownloadClientProviderType,
                    ClientName = c.DownloadClientName,
                    Settings = protector.Unprotect(c.DownloadClientSettings)
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to list the stuck download candidates.");
            throw;
        }
    }

    public async Task<IReadOnlyList<DownloadCleanupCandidate>> ListCleanupCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var candidates = await (
                from d in context.Downloads.AsNoTracking()
                join c in context.DownloadClients.AsNoTracking() on d.DownloadClientId equals c.Id
                where d.ClientRemovedAt == null &&
                    c.Enabled &&
                    c.Status != IntegrationHealthStatus.Unhealthy &&
                    c.Status != IntegrationHealthStatus.Stale &&
                    (DownloadStatuses.Failures.Contains(d.Status) || d.Status == DownloadStatus.Imported)
                select new
                {
                    DownloadId = d.Id,
                    d.ExternalId,
                    d.Title,
                    d.Status,
                    d.HeldByPolicy,
                    c.ProviderType,
                    ClientName = c.Name,
                    c.Settings,
                    c.CleanupDeleteFiles,
                    c.KeepFailedOnClient
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var results = new List<DownloadCleanupCandidate>();
            foreach (var c in candidates)
            {
                results.Add(new DownloadCleanupCandidate
                {
                    DownloadId = c.DownloadId,
                    ExternalId = c.ExternalId,
                    Title = c.Title,
                    Status = c.Status,
                    HeldByPolicy = c.HeldByPolicy,
                    ProviderType = c.ProviderType,
                    ClientName = c.ClientName,
                    Settings = protector.Unprotect(c.Settings),
                    CleanupDeleteFiles = c.CleanupDeleteFiles,
                    KeepFailedOnClient = c.KeepFailedOnClient
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to list the download cleanup candidates.");
            throw;
        }
    }

    public async Task<IReadOnlyList<Download>> ListClientResidentByContentItemsAsync(
        IReadOnlyList<Guid> contentItemIds,
        CancellationToken cancellationToken = default)
    {
        if (contentItemIds.Count == 0)
        {
            return [];
        }

        try
        {
            var distinctIds = contentItemIds.Distinct().ToList();

            return await (
                from d in context.Downloads.AsNoTracking()
                join scope in context.DownloadScopes.AsNoTracking() on d.Id equals scope.DownloadId
                join ci in context.ContentItems.AsNoTracking()
                    on scope.ContentHierarchyItemId equals ci.ContentHierarchyItemId
                where distinctIds.Contains(ci.Id) &&
                    d.ClientRemovedAt == null &&
                    d.ExternalId != ""
                select d)
                .Distinct()
                .Include(download => download.Scope)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The download repository failed to list the client-resident downloads for {Count} content items.",
                contentItemIds.Count);
            throw;
        }
    }

    public async Task<DownloadRetryResult> RetryAsync(
        Guid id,
        Guid? retriedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var affectedScope = new List<DownloadScopeRow>();
            var result = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var snapshot = await (
                        from d in context.Downloads.AsNoTracking()
                        where d.Id == id
                        select new { d.Title, d.Status })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (snapshot is null)
                    {
                        return new DownloadRetryResult { Outcome = DownloadRetryOutcome.NotFound };
                    }

                    if (!DownloadStatuses.Failures.Contains(snapshot.Status))
                    {
                        return new DownloadRetryResult { Outcome = DownloadRetryOutcome.Conflict };
                    }

                    var scopeNodeIds =
                        from scope in context.DownloadScopes.AsNoTracking()
                        where scope.DownloadId == id
                        select scope.ContentHierarchyItemId;

                    var contentItemId = await (
                        from node in context.ContentHierarchyItems.AsNoTracking()
                        join contentItem in context.ContentItems.AsNoTracking()
                            on node.Id equals contentItem.ContentHierarchyItemId
                        where scopeNodeIds.Contains(node.Id)
                        where node.ParentId == null || !scopeNodeIds.Contains(node.ParentId.Value)
                        select (Guid?)contentItem.Id)
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    var now = _time.GetUtcNow().UtcDateTime;
                    const string operatorRetryReason = "Operator retry";

                    var updated = await context.Downloads
                        .Where(d => d.Id == id && d.Status == snapshot.Status)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(d => d.Status, DownloadStatus.Cancelled)
                                .SetProperty(d => d.FailureReason, (DownloadFailureReason?)null)
                                .SetProperty(d => d.FailedAt, (DateTime?)null)
                                .SetProperty(d => d.StatusReason, (string?)operatorRetryReason)
                                .SetProperty(d => d.ModifiedAt, (DateTime?)now)
                                .SetProperty(d => d.ModifiedByUserId, retriedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (updated == 0)
                    {
                        return new DownloadRetryResult { Outcome = DownloadRetryOutcome.Conflict };
                    }

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "Retry",
                            EntityId = id.ToString(),
                            EntityType = nameof(Download),
                            Detail = $"Operator retry: {snapshot.Title} ({snapshot.Status} -> Cancelled; " +
                                "retry triggered)",
                            Timestamp = now,
                            UserId = retriedByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    affectedScope = await ListScopedContentAsync([id], cancellationToken).ConfigureAwait(false);
                    var affectedContentItemIds = ContentItemIdsOf(affectedScope);
                    await RecomputeActiveAcquisitionStatusAsync(
                        affectedContentItemIds,
                        now,
                        retriedByUserId,
                        cancellationToken).ConfigureAwait(false);

                    return new DownloadRetryResult
                    {
                        Outcome = DownloadRetryOutcome.Retried,
                        ContentItemId = contentItemId
                    };
                },
                cancellationToken).ConfigureAwait(false);

            if (result.Outcome == DownloadRetryOutcome.Retried)
            {
                await PublishUpdatedAsync(
                    id,
                    ["status", "failureReason", "failedAt", "statusReason"],
                    affectedScope,
                    cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to retry download {DownloadId}.", id);
            throw;
        }
    }

    public async Task<int> DeleteClearedFailedDownloadsAsync(
        DateTime failedBefore,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var topmostScopes = TopmostScopes();
                    var anchors =
                        from d in context.Downloads.AsNoTracking()
                        select new
                        {
                            d.Id,
                            d.Status,
                            d.DateCompleted,
                            AnchorTimestamp = d.FailedAt ?? d.CreatedAt,
                            WantedItemId = (
                                from scope in topmostScopes
                                where scope.DownloadId == d.Id
                                orderby scope.Id
                                select (Guid?)scope.ContentHierarchyItemId).FirstOrDefault(),
                        };
                    var ids = await anchors
                        .Where(d => DownloadStatuses.Failures.Contains(d.Status) &&
                            (d.AnchorTimestamp < failedBefore ||
                                (d.WantedItemId != null && anchors.Any(imported =>
                                    imported.Status == DownloadStatus.Imported &&
                                    imported.WantedItemId == d.WantedItemId &&
                                    imported.DateCompleted > d.AnchorTimestamp))))
                        .Select(d => d.Id)
                        .ToListAsync(ct)
                        .ConfigureAwait(false);
                    if (ids.Count == 0)
                    {
                        return 0;
                    }

                    var scope = await ListScopedContentAsync(ids, ct).ConfigureAwait(false);
                    var deleted = await context.Downloads
                        .Where(d => ids.Contains(d.Id) && DownloadStatuses.Failures.Contains(d.Status))
                        .ExecuteDeleteAsync(ct).ConfigureAwait(false);
                    if (deleted > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Integrations,
                                Operation = "Delete",
                                EntityType = nameof(Download),
                                Detail = $"Deleted {deleted} cleared failed downloads.",
                                Timestamp = _time.GetUtcNow().UtcDateTime
                            },
                            ct).ConfigureAwait(false);
                        await PublishDeletedAsync(ids, scope, ct).ConfigureAwait(false);
                    }

                    return deleted;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The download repository failed to delete the cleared failed downloads before {FailedBefore:O}.",
                failedBefore);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var affectedScope = new List<DownloadScopeRow>();
            var deleted = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var model = await (
                        from d in context.Downloads.AsNoTracking()
                        where d.Id == id
                        select new { d.Title })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    affectedScope = await ListScopedContentAsync([id], cancellationToken)
                        .ConfigureAwait(false);

                    var affectedContentItemIds = ContentItemIdsOf(affectedScope);

                    var deleted = await context.Downloads
                        .Where(d => d.Id == id)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (deleted > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Integrations,
                                Operation = "Delete",
                                EntityId = id.ToString(),
                                EntityType = nameof(Download),
                                Detail = $"Deleted download {model.Title}",
                                Timestamp = now,
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);

                        await RecomputeActiveAcquisitionStatusAsync(
                            affectedContentItemIds,
                            now,
                            deletedByUserId,
                            cancellationToken).ConfigureAwait(false);
                    }

                    return deleted > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (deleted)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        DownloadEventBuilder.BuildDownloadDeletedEvent(id, ImpactOf(affectedScope, id)),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return deleted;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download repository failed to delete download {DownloadId}.", id);
            throw;
        }
    }

    private async Task PublishUpdatedAsync(
        Guid downloadId,
        IReadOnlyCollection<string> changedFields,
        IReadOnlyCollection<DownloadScopeRow> scope,
        CancellationToken cancellationToken)
    {
        await transactionManager.OnCommittedAsync(
            token => eventBus.PublishAsync(
                DownloadEventBuilder.BuildDownloadUpdatedEvent(downloadId, changedFields, ImpactOf(scope, downloadId)),
                token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishDeletedAsync(
        IReadOnlyCollection<Guid> downloadIds,
        IReadOnlyCollection<DownloadScopeRow> scope,
        CancellationToken cancellationToken)
    {
        foreach (var downloadId in downloadIds)
        {
            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    DownloadEventBuilder.BuildDownloadDeletedEvent(downloadId, ImpactOf(scope, downloadId)),
                    token),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PublishStatusTransitionEventsAsync(
        Guid downloadId,
        Guid? releaseId,
        DownloadStatus from,
        DownloadStatus to,
        string? statusReason,
        DownloadFailureReason? failureReason,
        IReadOnlyCollection<DownloadScopeRow> scope,
        CancellationToken cancellationToken)
    {
        var impact = ImpactOf(scope, downloadId);
        if (to == DownloadStatus.Downloaded && from != DownloadStatus.Downloaded)
        {
            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    DownloadEventBuilder.BuildDownloadCompletedEvent(downloadId, releaseId, impact),
                    token),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (DownloadStatuses.Failures.Contains(to) && from != to)
        {
            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    DownloadEventBuilder.BuildDownloadFailedEvent(
                        downloadId,
                        releaseId,
                        failureReason ?? DownloadFailureReason.DownloadErrored,
                        impact),
                    token),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (to == DownloadStatus.Stalled && from != DownloadStatus.Stalled)
        {
            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    DownloadEventBuilder.BuildDownloadStalledEvent(
                        downloadId,
                        releaseId,
                        statusReason ?? "No progress reported",
                        impact),
                    token),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await PublishUpdatedAsync(downloadId, RefreshedFields, scope, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HashSet<Guid>> ListOverlappingNodeIdsAsync(
        IReadOnlyCollection<Guid> scopeIds,
        CancellationToken cancellationToken)
    {
        var nodes = await ListFulfillmentScopeNodesAsync(scopeIds, cancellationToken).ConfigureAwait(false);
        var overlappingNodeIds = nodes.Keys.ToHashSet();
        var parentIds = nodes.Values.Where(node => node.ParentId is not null)
            .Select(node => node.ParentId!.Value).Distinct().ToList();
        while (parentIds.Count > 0)
        {
            var pending = parentIds.Where(overlappingNodeIds.Add).ToList();
            parentIds = await context.ContentHierarchyItems.AsNoTracking()
                .Where(node => pending.Contains(node.Id) && node.ParentId != null)
                .Select(node => node.ParentId!.Value).Distinct()
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        return overlappingNodeIds;
    }

    private async Task<Dictionary<Guid, FulfillmentScopeNode>> ListFulfillmentScopeNodesAsync(
        IReadOnlyCollection<Guid> scopeIds,
        CancellationToken cancellationToken)
    {
        var nodes = await context.ContentHierarchyItems.AsNoTracking()
            .Where(node => scopeIds.Contains(node.Id))
            .Select(node => new FulfillmentScopeNode(node.Id, node.ParentId,
                node.ContentItem != null ? node.ContentItem.Fulfillment : null))
            .ToDictionaryAsync(node => node.Id, cancellationToken).ConfigureAwait(false);
        var parentIds = nodes.Keys.ToList();
        while (parentIds.Count > 0)
        {
            var children = await context.ContentHierarchyItems.AsNoTracking()
                .Where(node => node.ParentId != null && parentIds.Contains(node.ParentId.Value))
                .Select(node => new FulfillmentScopeNode(node.Id, node.ParentId,
                    node.ContentItem != null ? node.ContentItem.Fulfillment : null))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            parentIds = [.. children.Where(node => nodes.TryAdd(node.Id, node)).Select(node => node.Id)];
        }

        return nodes;
    }

    private sealed record AcquisitionStatusChange(Guid ContentItemId, DownloadStatus? Next);

    private sealed record FulfillmentScopeNode(Guid Id, Guid? ParentId, FulfillmentStatus? Fulfillment);

    private sealed record DownloadScopeRow(
        Guid DownloadId,
        Guid HierarchyItemId,
        Guid ContentItemId,
        Guid MetadataContainerId,
        Guid? ParentId,
        string ContentType,
        string Level,
        Guid? RootFolderId,
        Guid? QualityTierListId,
        MonitoringMode EffectiveMonitoring,
        FulfillmentStatus Fulfillment,
        Guid? RequestedViaWishlistId);
}
