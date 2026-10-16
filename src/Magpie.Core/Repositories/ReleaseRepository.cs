using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class ReleaseRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IDownloadRepository downloadRepository,
    ILogger<ReleaseRepository> logger,
    TimeProvider timeProvider) : IReleaseRepository
{
    private const int ParameterBatchSize = 1_000;

    private const string CreateSavepoint = "release_create";

    private readonly TimeProvider _time = timeProvider;

    private static void NormalizeIdentityKeys(Release release)
    {
        var normalized = new List<ReleaseIdentityKey>();
        var seen = new HashSet<(string Authority, string Value)>();
        foreach (var key in release.IdentityKeys)
        {
            if (string.IsNullOrWhiteSpace(key.Authority) || string.IsNullOrWhiteSpace(key.Value))
            {
                continue;
            }

            var authority = key.Authority.Trim();
            var value = key.Value.Trim();
            if (seen.Add((authority, value)))
            {
                normalized.Add(new ReleaseIdentityKey { Authority = authority, Value = value });
            }
        }

        release.IdentityKeys.Clear();
        foreach (var key in normalized)
        {
            key.Ordinal = release.IdentityKeys.Count;
            release.IdentityKeys.Add(key);
        }
    }

    private static void NormalizeExternalIds(Release release)
    {
        var incoming = release.ExternalIds.ToList();
        release.ExternalIds.Clear();
        foreach (var externalId in incoming)
        {
            release.ExternalIds.Add(new ExternalId
            {
                Source = externalId.Source,
                Value = externalId.Value,
                Ordinal = externalId.Ordinal,
            });
        }
    }

    private static string SourceKey(ReleaseSource source)
    {
        var selector = !string.IsNullOrWhiteSpace(source.ExternalId)
            ? $"{source.IndexerId:D}\nid:{source.ExternalId.Trim()}"
            : $"{source.IndexerId:D}\nurl:{source.InfoUrl}";
        if (source.IndexerConnectionId is { } indexerConnectionId)
        {
            selector += $"\nconnection:{indexerConnectionId:D}";
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selector))).ToLowerInvariant();
    }

    private static ReleaseSource SourceFromRelease(Release release)
    {
        return new()
        {
            Key = string.Empty,
            IndexerId = release.IndexerId,
            Protocol = release.Protocol,
            ExternalId = release.ExternalId,
            Title = release.Title,
            PublishDate = release.PublishDate,
            Size = release.Size,
            InfoUrl = release.InfoUrl,
            Categories = [.. release.Categories],
            Languages = [.. release.Languages],
            Properties = release.Properties,
        };
    }

    private static void EnsureReleaseSources(Release release, DateTime now)
    {
        var incoming = release.Sources.Count == 0 ? [SourceFromRelease(release)] : release.Sources.ToList();
        release.Sources.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in incoming)
        {
            source.Key = SourceKey(source);
            if (!seen.Add(source.Key))
            {
                continue;
            }

            source.ReleaseId = release.Id;
            source.CreatedAt = source.CreatedAt == default ? now : source.CreatedAt;
            source.ObservedAt = now;
            release.Sources.Add(source);
        }
    }

    private static List<ReleaseIdentityKey> SnapshotReleaseIdentityKeys(IEnumerable<ReleaseIdentityKey> keys)
    {
        return
        [
            ..keys.Select(key => new ReleaseIdentityKey
            {
                Authority = key.Authority,
                Value = key.Value,
                Ordinal = key.Ordinal,
            })
        ];
    }

    public async Task<IEnumerable<Release>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.Releases
                .AsNoTracking()
                .AsSplitQuery()
                .Include(r => r.ExternalIds)
                .Include(r => r.IdentityKeys)
                .Include(r => r.Sources)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The release repository failed to list releases.");
            throw;
        }
    }

    public async Task<Release?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Releases
                .AsNoTracking()
                .AsSplitQuery()
                .Include(r => r.ExternalIds)
                .Include(r => r.IdentityKeys)
                .Include(r => r.Sources)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The release repository failed to read release {ReleaseId}.", id);
            throw;
        }
    }

    private async Task<List<Release>> ListByIdentityKeysAsync(
        IReadOnlyCollection<ReleaseIdentityKey> identityKeys,
        CancellationToken cancellationToken)
    {
        var releaseIds = await ListReleaseIdsByIdentityKeysAsync(identityKeys, cancellationToken)
            .ConfigureAwait(false);
        if (releaseIds.Count == 0)
        {
            return [];
        }

        return await context.Releases
            .AsNoTracking()
            .AsSplitQuery()
            .Include(candidate => candidate.ExternalIds)
            .Include(candidate => candidate.IdentityKeys)
            .Include(candidate => candidate.Sources)
            .Where(candidate => releaseIds.Contains(candidate.Id))
            .OrderByDescending(candidate => candidate.ObservedAt)
            .ThenByDescending(candidate => candidate.CreatedAt)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<List<Release>> FindUpsertMatchesAsync(Release release, CancellationToken cancellationToken)
    {
        if (release.IdentityKeys.Count > 0)
        {
            var matches = await ListByIdentityKeysAsync([.. release.IdentityKeys], cancellationToken)
                .ConfigureAwait(false);
            if (matches.Count > 0)
            {
                return matches;
            }
        }

        if (release.IndexerId is not { } indexerId || string.IsNullOrWhiteSpace(release.ExternalId))
        {
            return [];
        }

        var byProvider = (await QueryAsync(
                new ReleaseQuery { IndexerId = indexerId, ExternalId = release.ExternalId },
                new PagingParameter { PageNumber = 1, PageSize = 1 },
                null,
                cancellationToken)
            .ConfigureAwait(false)).Items.FirstOrDefault();
        if (byProvider is null)
        {
            return [];
        }

        return [byProvider];
    }

    private async Task<List<Guid>> ListReleaseIdsByIdentityKeysAsync(
        IReadOnlyCollection<ReleaseIdentityKey> identityKeys,
        CancellationToken cancellationToken)
    {
        var keys = identityKeys
            .Where(key => !string.IsNullOrWhiteSpace(key.Authority) && !string.IsNullOrWhiteSpace(key.Value))
            .Select(key => (Authority: key.Authority.Trim(), Value: key.Value.Trim()))
            .Distinct()
            .ToList();
        if (keys.Count == 0)
        {
            return [];
        }

        var authorities = keys.Select(key => key.Authority).Distinct().ToList();
        var values = keys.Select(key => key.Value).Distinct().ToList();
        var keySet = keys.ToHashSet();
        var candidateRows = await (
            from key in context.ReleaseIdentityKeys.AsNoTracking()
            where key.ReleaseId != null
            where authorities.Contains(key.Authority) && values.Contains(key.Value)
            select new
            {
                ReleaseId = key.ReleaseId!.Value,
                key.Authority,
                key.Value
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var releaseIds = candidateRows
            .Where(row => keySet.Contains((row.Authority, row.Value)))
            .Select(row => row.ReleaseId)
            .Distinct()
            .ToList();
        return releaseIds;
    }

    private async Task CoalesceIdentityMatchesAsync(
        Release incoming,
        Release canonical,
        IReadOnlyCollection<Release> matches,
        CancellationToken cancellationToken)
    {
        var aliasIds = matches.Where(match => match.Id != canonical.Id).Select(match => match.Id).ToArray();
        if (aliasIds.Length == 0)
        {
            return;
        }

        foreach (var identity in matches.SelectMany(match => match.IdentityKeys))
        {
            incoming.IdentityKeys.Add(new ReleaseIdentityKey
            {
                Authority = identity.Authority,
                Value = identity.Value,
                Ordinal = incoming.IdentityKeys.Count,
            });
        }

        var sourceRows = await context.ReleaseSources
            .AsNoTracking()
            .Where(source => source.ReleaseId == canonical.Id || aliasIds.Contains(source.ReleaseId))
            .OrderBy(source => source.ReleaseId == canonical.Id ? 0 : 1)
            .ThenBy(source => source.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var sourceIdsByKey = sourceRows
            .Where(source => source.ReleaseId == canonical.Id)
            .ToDictionary(source => source.Key, source => source.Id, StringComparer.Ordinal);
        var mergedSourceIds = new Dictionary<Guid, Guid>();
        foreach (var source in sourceRows.Where(source => source.ReleaseId != canonical.Id))
        {
            if (sourceIdsByKey.TryGetValue(source.Key, out var retainedSourceId))
            {
                mergedSourceIds.Add(source.Id, retainedSourceId);
                continue;
            }

            await context.ReleaseSources
                .Where(candidate => candidate.Id == source.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(candidate => candidate.ReleaseId, canonical.Id),
                    cancellationToken)
                .ConfigureAwait(false);
            sourceIdsByKey.Add(source.Key, source.Id);
        }

        await downloadRepository.ReassignReleasesAsync(
            aliasIds.ToDictionary(aliasId => aliasId, _ => canonical.Id),
            mergedSourceIds,
            cancellationToken).ConfigureAwait(false);
        var deletedSourceIds = mergedSourceIds.Keys.ToArray();
        await context.ReleaseSources
            .Where(candidate => deletedSourceIds.Contains(candidate.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await context.ExternalIds
            .Where(externalId => externalId.ReleaseId != null && aliasIds.Contains(externalId.ReleaseId.Value))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(externalId => externalId.ReleaseId, (Guid?)canonical.Id),
                cancellationToken)
            .ConfigureAwait(false);

        var canonicalScopeIds = await context.ReleaseScopes
            .AsNoTracking()
            .Where(scope => scope.ReleaseId == canonical.Id)
            .Select(scope => scope.ContentHierarchyItemId)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);
        var aliasScopes = await context.ReleaseScopes
            .AsNoTracking()
            .Where(scope => aliasIds.Contains(scope.ReleaseId))
            .GroupBy(scope => scope.ContentHierarchyItemId)
            .Select(group => new
            {
                ContentHierarchyItemId = group.Key,
                Unmatched = group.Any(scope => scope.Status == ReleaseScopeStatus.Unmatched),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var aliasScope in aliasScopes)
        {
            if (canonicalScopeIds.Add(aliasScope.ContentHierarchyItemId))
            {
                context.ReleaseScopes.Add(new ReleaseScope
                {
                    ReleaseId = canonical.Id,
                    ContentHierarchyItemId = aliasScope.ContentHierarchyItemId,
                    Status = aliasScope.Unmatched ? ReleaseScopeStatus.Unmatched : ReleaseScopeStatus.Matched,
                });
            }
            else if (aliasScope.Unmatched)
            {
                await context.ReleaseScopes
                    .Where(scope => scope.ReleaseId == canonical.Id &&
                        scope.ContentHierarchyItemId == aliasScope.ContentHierarchyItemId)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(scope => scope.Status, ReleaseScopeStatus.Unmatched),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await context.ReleaseScopes
            .Where(scope => aliasIds.Contains(scope.ReleaseId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        var aliasTagNames = await context.Tags
            .AsNoTracking()
            .Where(tag => tag.Releases.Any(release => aliasIds.Contains(release.Id)))
            .Select(tag => tag.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var canonicalTagNames = await context.Tags
            .AsNoTracking()
            .Where(tag => tag.Releases.Any(release => release.Id == canonical.Id))
            .Select(tag => tag.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var missingTagNames = aliasTagNames.Except(canonicalTagNames, StringComparer.Ordinal).ToArray();
        if (missingTagNames.Length > 0)
        {
            var canonicalEntity = await context.Releases
                .FirstAsync(release => release.Id == canonical.Id, cancellationToken)
                .ConfigureAwait(false);
            var missingTags = await context.Tags
                .Where(tag => missingTagNames.Contains(tag.Name))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var tag in missingTags)
            {
                tag.Releases.Add(canonicalEntity);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.ChangeTracker.Clear();
        await context.Releases
            .Where(release => aliasIds.Contains(release.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PagedList<Release>> QueryAsync(
        ReleaseQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var releases = context.Releases.AsNoTracking().AsQueryable();

            if (query.Ids is { Count: > 0 })
            {
                releases = releases.Where(r => query.Ids.Contains(r.Id));
            }

            if (query.IndexerId is { } indexerId && query.ExternalId is { } externalId)
            {
                releases = releases.Where(r =>
                    (r.IndexerId == indexerId && r.ExternalId == externalId) ||
                    r.Sources.Any(source => source.IndexerId == indexerId && source.ExternalId == externalId));
            }
            else if (query.IndexerId is not null)
            {
                releases = releases.Where(r => r.IndexerId == query.IndexerId ||
                    r.Sources.Any(source => source.IndexerId == query.IndexerId));
            }

            var contentNodeIds = new List<Guid>();
            if (query.ContentHierarchyItemId is { } contentNodeId)
            {
                contentNodeIds.Add(contentNodeId);
            }

            if (query.ContentHierarchyItemIds is { Count: > 0 })
            {
                contentNodeIds.AddRange(query.ContentHierarchyItemIds);
            }

            contentNodeIds = [.. contentNodeIds.Distinct()];
            if (contentNodeIds.Count > 0)
            {
                var releaseIds = (
                    from c in context.ReleaseScopes.AsNoTracking()
                    where contentNodeIds.Contains(c.ContentHierarchyItemId)
                    where query.IncludeUnmatched || c.Status == ReleaseScopeStatus.Matched
                    select c.ReleaseId)
                    .Distinct();
                releases = releases.Where(r => releaseIds.Contains(r.Id));
            }

            if (query.Title is not null)
            {
                releases = releases.Where(r => r.Title.Contains(query.Title));
            }

            if (query.Category is not null)
            {
                releases = releases.Where(r => r.Categories.Contains(query.Category));
            }

            if (query.IndexerId is null && query.ExternalId is not null)
            {
                releases = releases.Where(r => r.ExternalId == query.ExternalId);
            }

            if (query.PublishDate?.Min is not null)
            {
                releases = releases.Where(r => r.PublishDate >= query.PublishDate.Min);
            }

            if (query.PublishDate?.Max is not null)
            {
                releases = releases.Where(r => r.PublishDate <= query.PublishDate.Max);
            }

            if (query.ObservedAt?.Min is { } observedAtMin)
            {
                releases = releases.Where(release => release.ObservedAt >= observedAtMin);
            }

            if (query.ObservedAt?.Max is { } observedAtMax)
            {
                releases = releases.Where(release => release.ObservedAt <= observedAtMax);
            }

            if (query.Tags is not null && query.Tags.Any())
            {
                releases = releases.Where(r => context.Tags
                    .Where(t => query.Tags.Contains(t.Name))
                    .SelectMany(t => t.Releases)
                    .Any(tr => tr.Id == r.Id));
            }

            var totalCount = await releases.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortProperty = context.Model.FindEntityType(typeof(Release))!.GetProperties()
                .Where(property => !property.IsShadowProperty() &&
                    (property.ClrType.IsValueType || property.ClrType == typeof(string) ||
                        property.ClrType == typeof(Uri)))
                .Select(property => property.Name)
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                releases = sort.Direction == SortDirection.Ascending
                    ? releases.OrderBy(r => EF.Property<object>(r, sortProperty)).ThenBy(r => r.Id)
                    : releases.OrderByDescending(r => EF.Property<object>(r, sortProperty)).ThenBy(r => r.Id);
            }
            else
            {
                releases = releases.OrderByDescending(r => r.PublishDate).ThenBy(r => r.Id);
            }

            releases = releases.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            var items = await releases
                .AsSplitQuery()
                .Include(r => r.ExternalIds)
                .Include(r => r.IdentityKeys)
                .Include(r => r.Sources)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

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
            logger.LogError(ex, "The release repository failed to query releases.");
            throw;
        }
    }

    public async Task<int> MarkObservedAsync(
        IReadOnlyCollection<Guid> releaseIds,
        CancellationToken cancellationToken = default)
    {
        if (releaseIds.Count == 0)
        {
            return 0;
        }

        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var distinctReleaseIds = releaseIds.Distinct().ToList();
            var updated = 0;
            foreach (var batch in distinctReleaseIds.Chunk(ParameterBatchSize))
            {
                updated += await context.Releases
                    .Where(r => batch.Contains(r.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.ObservedAt, now), cancellationToken)
                    .ConfigureAwait(false);
            }

            return updated;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The release repository failed to mark {ReleaseCount} releases observed.",
                releaseIds.Count);
            throw;
        }
    }

    public async Task<Release> CreateAsync(Release model, Guid? createdByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    if (model.IndexerId is { } indexerId && !string.IsNullOrWhiteSpace(model.ExternalId))
                    {
                        await context.Indexers
                            .Where(indexer => indexer.Id == indexerId)
                            .ExecuteUpdateAsync(
                                setters => setters.SetProperty(indexer => indexer.Name, indexer => indexer.Name),
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (await UpdateAsync(model, createdByUserId, cancellationToken).ConfigureAwait(false))
                        {
                            return (await GetByIdAsync(model.Id, cancellationToken).ConfigureAwait(false))!;
                        }
                    }

                    var now = _time.GetUtcNow().UtcDateTime;
                    model.Id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id;
                    model.CreatedAt = now;
                    model.ObservedAt = now;
                    model.CreatedByUserId = createdByUserId;
                    NormalizeIdentityKeys(model);
                    NormalizeExternalIds(model);
                    EnsureReleaseSources(model, now);

                    var transaction = context.Database.CurrentTransaction;
                    var savepointTaken = transaction is { SupportsSavepoints: true };
                    if (savepointTaken)
                    {
                        await transaction!.CreateSavepointAsync(CreateSavepoint, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    try
                    {
                        context.Releases.Add(model);
                        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (DbUpdateException)
                    {
                        if (savepointTaken)
                        {
                            await transaction!.RollbackToSavepointAsync(CreateSavepoint, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        context.ChangeTracker.Clear();
                        throw;
                    }

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(Release),
                            Detail = $"Created release {model.Title}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The release repository failed to create release {ReleaseTitle}.", model.Title);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(Release model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    NormalizeIdentityKeys(model);
                    EnsureReleaseSources(model, now);
                    var matches = await FindUpsertMatchesAsync(model, cancellationToken)
                        .ConfigureAwait(false);
                    var canonical = matches.FirstOrDefault(match => match.Id == model.Id);
                    if (canonical is null && model.Id != Guid.Empty)
                    {
                        canonical = await context.Releases
                            .AsNoTracking()
                            .AsSplitQuery()
                            .Include(release => release.ExternalIds)
                            .Include(release => release.IdentityKeys)
                            .Include(release => release.Sources)
                            .FirstOrDefaultAsync(release => release.Id == model.Id, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    canonical ??= matches.FirstOrDefault();
                    if (canonical is null)
                    {
                        return false;
                    }

                    model.Id = canonical.Id;
                    if (matches.All(match => match.Id != canonical.Id))
                    {
                        matches.Insert(0, canonical);
                    }

                    if (matches.Count > 1)
                    {
                        await CoalesceIdentityMatchesAsync(model, canonical, matches, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    var updated = await context.Releases
                        .Where(r => r.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(r => r.IndexerId, model.IndexerId)
                                .SetProperty(r => r.Protocol, model.Protocol)
                                .SetProperty(r => r.ExternalId, model.ExternalId)
                                .SetProperty(r => r.Title, model.Title)
                                .SetProperty(r => r.PublishDate, model.PublishDate)
                                .SetProperty(r => r.Size, model.Size)
                                .SetProperty(r => r.InfoUrl, model.InfoUrl)
                                .SetProperty(r => r.Categories, model.Categories)
                                .SetProperty(r => r.Languages, model.Languages)
                                .SetProperty(r => r.Properties, model.Properties)
                                .SetProperty(r => r.ModifiedAt, now)
                                .SetProperty(r => r.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await MergeReleaseIdentityKeysAsync(model, cancellationToken).ConfigureAwait(false);
                        await MergeReleaseSourcesAsync(model, now, cancellationToken).ConfigureAwait(false);
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Integrations,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(Release),
                                Detail = $"Updated release {model.Title}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The release repository failed to update release {ReleaseId}.", model.Id);
            throw;
        }
    }

    private async Task MergeReleaseIdentityKeysAsync(Release release, CancellationToken cancellationToken)
    {
        var incomingKeys = SnapshotReleaseIdentityKeys(release.IdentityKeys);
        var existingKeys = await context.ReleaseIdentityKeys
            .AsNoTracking()
            .Where(k => k.ReleaseId == release.Id)
            .OrderBy(k => k.Ordinal)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var normalizedRelease = new Release
        {
            IndexerId = release.IndexerId,
            ExternalId = release.ExternalId,
            Protocol = release.Protocol,
            Title = release.Title,
            Size = release.Size,
            InfoUrl = release.InfoUrl,
        };
        foreach (var key in SnapshotReleaseIdentityKeys(existingKeys).Concat(incomingKeys))
        {
            normalizedRelease.IdentityKeys.Add(key);
        }

        NormalizeIdentityKeys(normalizedRelease);
        var normalizedKeys = SnapshotReleaseIdentityKeys(normalizedRelease.IdentityKeys);

        var trackedKeys = context.ChangeTracker
            .Entries<ReleaseIdentityKey>()
            .Where(entry => entry.Entity.ReleaseId == release.Id)
            .ToArray();
        foreach (var entry in trackedKeys)
        {
            entry.State = EntityState.Detached;
        }

        await context.ReleaseIdentityKeys
            .Where(k => k.ReleaseId == release.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var key in normalizedKeys)
        {
            key.ReleaseId = release.Id;
            context.ReleaseIdentityKeys.Add(key);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MergeReleaseSourcesAsync(
        Release release,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var existingSources = await context.ReleaseSources
            .AsNoTracking()
            .Where(source => source.ReleaseId == release.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var source in release.Sources)
        {
            var matches = existingSources.Where(candidate =>
                string.Equals(candidate.Key, source.Key, StringComparison.Ordinal) ||
                (candidate.IndexerId == source.IndexerId &&
                    candidate.IndexerConnectionId == source.IndexerConnectionId &&
                    ((!string.IsNullOrWhiteSpace(source.ExternalId) &&
                        string.Equals(candidate.ExternalId, source.ExternalId, StringComparison.Ordinal)) ||
                        (string.IsNullOrWhiteSpace(source.ExternalId) && Equals(candidate.InfoUrl, source.InfoUrl)))))
                .ToList();
            var existing = matches.FirstOrDefault(candidate =>
                    string.Equals(candidate.Key, source.Key, StringComparison.Ordinal)) ??
                matches.FirstOrDefault();
            if (existing is null)
            {
                context.ReleaseSources.Add(new ReleaseSource
                {
                    ReleaseId = release.Id,
                    Key = source.Key,
                    IndexerId = source.IndexerId,
                    IndexerConnectionId = source.IndexerConnectionId,
                    Protocol = source.Protocol,
                    ExternalId = source.ExternalId,
                    Title = source.Title,
                    PublishDate = source.PublishDate,
                    Size = source.Size,
                    InfoUrl = source.InfoUrl,
                    Categories = [.. source.Categories],
                    Languages = [.. source.Languages],
                    Properties = source.Properties,
                    CreatedAt = observedAt,
                    ObservedAt = observedAt,
                });
                continue;
            }

            var duplicates = matches.Where(candidate => candidate.Id != existing.Id).ToList();
            await downloadRepository.ReassignReleasesAsync(
                new Dictionary<Guid, Guid>(),
                duplicates.ToDictionary(duplicate => duplicate.Id, _ => existing.Id),
                cancellationToken).ConfigureAwait(false);
            foreach (var duplicate in duplicates)
            {
                await context.ReleaseSources
                    .Where(candidate => candidate.Id == duplicate.Id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                existingSources.Remove(duplicate);
            }

            await context.ReleaseSources
                .Where(candidate => candidate.Id == existing.Id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.Key, source.Key)
                        .SetProperty(candidate => candidate.IndexerId, source.IndexerId)
                        .SetProperty(candidate => candidate.IndexerConnectionId, source.IndexerConnectionId)
                        .SetProperty(candidate => candidate.Protocol, source.Protocol)
                        .SetProperty(candidate => candidate.ExternalId, source.ExternalId)
                        .SetProperty(candidate => candidate.Title, source.Title)
                        .SetProperty(candidate => candidate.PublishDate, source.PublishDate)
                        .SetProperty(candidate => candidate.Size, source.Size)
                        .SetProperty(candidate => candidate.InfoUrl, source.InfoUrl)
                        .SetProperty(candidate => candidate.Categories, source.Categories)
                        .SetProperty(candidate => candidate.Languages, source.Languages)
                        .SetProperty(candidate => candidate.Properties, source.Properties)
                        .SetProperty(candidate => candidate.ObservedAt, observedAt),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteWhereAsync(ReleaseQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            var releases = new List<Release>();
            var pageNumber = 1;
            while (true)
            {
                var page = await QueryAsync(
                    query,
                    new() { PageNumber = pageNumber, PageSize = ParameterBatchSize },
                    null,
                    cancellationToken).ConfigureAwait(false);
                var pageItems = page.Items.ToList();
                releases.AddRange(pageItems);
                if (pageItems.Count < ParameterBatchSize)
                {
                    break;
                }

                pageNumber++;
            }

            var unmatchedReleaseIds = await context.ReleaseScopes
                .AsNoTracking()
                .Where(scope => scope.Status == ReleaseScopeStatus.Unmatched)
                .Select(scope => scope.ReleaseId)
                .ToHashSetAsync(cancellationToken)
                .ConfigureAwait(false);
            releases.RemoveAll(release => unmatchedReleaseIds.Contains(release.Id));
            if (releases.Count == 0)
            {
                return 0;
            }

            return await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var ids = releases.Select(release => release.Id).ToList();
                    var deleted = 0;
                    foreach (var batch in ids.Chunk(ParameterBatchSize))
                    {
                        await downloadRepository.DetachReleasesAsync(batch, ct).ConfigureAwait(false);
                        deleted += await context.Releases
                            .Where(release => batch.Contains(release.Id))
                            .ExecuteDeleteAsync(ct)
                            .ConfigureAwait(false);
                    }

                    foreach (var release in releases)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Integrations,
                                Operation = "Delete",
                                EntityId = release.Id.ToString(),
                                EntityType = nameof(Release),
                                Detail = $"Deleted release {release.Title}",
                                Timestamp = now
                            },
                            ct).ConfigureAwait(false);
                    }

                    return deleted;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The release repository failed to delete releases by query.");
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var model = await (
                        from r in context.Releases.AsNoTracking()
                        where r.Id == id
                        select new { r.Title })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    await downloadRepository.DetachReleasesAsync([id], cancellationToken).ConfigureAwait(false);
                    var deleted = await context.Releases
                        .Where(r => r.Id == id)
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
                                EntityType = nameof(Release),
                                Detail = $"Deleted release {model.Title}",
                                Timestamp = now,
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return deleted > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The release repository failed to delete release {ReleaseId}.", id);
            throw;
        }
    }
}
