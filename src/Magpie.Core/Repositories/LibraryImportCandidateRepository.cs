using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Core.Jobs;
using Magpie.Data;
using Magpie.Data.Configuration;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Import;
using Magpie.Models.Paging;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class LibraryImportCandidateRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<LibraryImportCandidateRepository> logger,
    TimeProvider timeProvider) : ILibraryImportCandidateRepository
{
    private const int BulkPageSize = 1_000;

    private readonly TimeProvider _time = timeProvider;

    /// <inheritdoc />
    public async Task<bool> TryReclaimInactiveImportAsync(
        Guid candidateId,
        DateTime inactivityCutoff,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async ct =>
            {
                var locked = await context.LibraryImportCandidates
                    .Where(candidate => candidate.Id == candidateId &&
                        (candidate.Status == LibraryImportCandidateStatus.Queued ||
                            candidate.Status == LibraryImportCandidateStatus.Importing) &&
                        (candidate.ModifiedAt ?? candidate.CreatedAt) <= inactivityCutoff)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            candidate => candidate.ModifiedAt,
                            candidate => candidate.ModifiedAt),
                        ct)
                    .ConfigureAwait(false);
                if (locked == 0)
                {
                    return false;
                }

                var executions = await context.JobExecutions.AsNoTracking()
                    .Where(execution => execution.JobType == MagpieJobs.Types.RootFolderContentImportItem &&
                        (execution.Status == JobExecutionStatus.Queued ||
                            execution.Status == JobExecutionStatus.Running ||
                            execution.Status == JobExecutionStatus.CancellationRequested))
                    .Select(execution => execution.Parameters).ToListAsync(ct).ConfigureAwait(false);
                if (executions.Any(parameters =>
                    parameters.TryGetValue(MagpieJobs.Parameters.CandidateId, out var rawId) &&
                    Guid.TryParse(rawId, out var id) && id == candidateId))
                {
                    return false;
                }

                var target = await context.LibraryImportCandidates.AsNoTracking()
                    .Where(candidate => candidate.Id == candidateId)
                    .Select(candidate => candidate.ContentHierarchyItemId)
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (target is { } itemId && await context.JobExecutions.AsNoTracking()
                    .AnyAsync(execution => (execution.JobType == MagpieJobs.Types.ContentMetadataRefreshItem ||
                            execution.JobType == MagpieJobs.Types.RootFolderContentImportResume) &&
                        (execution.Status == JobExecutionStatus.Queued ||
                            execution.Status == JobExecutionStatus.Running ||
                            execution.Status == JobExecutionStatus.CancellationRequested) &&
                        EF.Property<string>(execution, JobExecutionReferenceColumns.ParameterContentHierarchyItemId) ==
                            itemId.ToString("D"),
                            ct).ConfigureAwait(false))
                {
                    return false;
                }

                return await MarkImportFailedAsync(
                    candidateId,
                    "The inactive import has no queued or active execution. The file can be rescanned.",
                    userId,
                    ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The library import repository failed to recover candidate {CandidateId}.",
                candidateId);
            throw;
        }
    }

    private static string ResolveGroupKey(LibraryImportCandidate candidate)
    {
        if (candidate.LayoutKey is { } layoutKey)
        {
            return layoutKey;
        }

        if (candidate.Matches.Count == 1)
        {
            var match = candidate.Matches[0];
            return $"match:{candidate.ContentType}:{match.ProviderType}:{match.ExternalIdKey}:{match.ExternalId}";
        }

        var title = FirstNonEmptySignal(candidate, ParsedFileSignalKeys.RootTitle)
            ?? FirstNonEmptySignal(candidate, ParsedFileSignalKeys.TitleToken)
            ?? FirstNonEmptySignal(candidate, ParsedFileSignalKeys.Title)
            ?? Path.GetFileNameWithoutExtension(candidate.FilePath)
            ?? string.Empty;

        return $"query:{candidate.ContentType}:{title.Trim().ToLowerInvariant()}";
    }

    private static string? FirstNonEmptySignal(LibraryImportCandidate candidate, string key) =>
        candidate.ParsedSignals.TryGetValue(key, out var values)
            ? values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            : null;

    public async Task<IEnumerable<LibraryImportCandidate>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.LibraryImportCandidates
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The library import repository failed to list library import matches.");
            throw;
        }
    }

    public async Task<LibraryImportCandidate?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.LibraryImportCandidates
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The library import repository failed to read library import candidate {LibraryImportCandidateId}.",
                id);
            throw;
        }
    }

    private IQueryable<LibraryImportCandidate> ApplyCriteria(LibraryImportCandidateQuery query)
    {
        var matches = context.LibraryImportCandidates.AsNoTracking().AsQueryable();

        if (query.RootFolderId is not null)
        {
            matches = matches.Where(m => m.RootFolderId == query.RootFolderId);
        }

        if (query.ScanRunId is not null)
        {
            matches = matches.Where(m => m.ScanRunId == query.ScanRunId);
        }

        if (query.Status is not null)
        {
            matches = matches.Where(m => m.Status == query.Status);
        }

        if (query.Statuses is { Count: > 0 })
        {
            var statuses = query.Statuses as LibraryImportCandidateStatus[] ?? [.. query.Statuses];
            matches = matches.Where(m => statuses.Contains(m.Status));
        }

        if (query.HasMatches is not null)
        {
            matches = query.HasMatches.Value
                ? matches.Where(m => m.MatchCount > 0)
                : matches.Where(m => m.MatchCount == 0);
        }

        if (query.ContentHierarchyItemIds is not null)
        {
            var targetIds = query.ContentHierarchyItemIds as Guid[] ?? [.. query.ContentHierarchyItemIds];

            matches = matches.Where(m => m.ContentHierarchyItemId != null &&
                EF.Parameter(targetIds).Contains(m.ContentHierarchyItemId.Value));
        }

        if (query.ExcludeIds is { Count: > 0 })
        {
            var excludeIds = query.ExcludeIds as Guid[] ?? [.. query.ExcludeIds];
            matches = matches.Where(m => !excludeIds.Contains(m.Id));
        }

        if (query.GroupKey is not null)
        {
            matches = matches.Where(m => m.GroupKey == query.GroupKey);
        }

        if (query.ExcludeGroupKeys is { Count: > 0 })
        {
            var excludeGroupKeys = query.ExcludeGroupKeys as string[] ?? [.. query.ExcludeGroupKeys];
            matches = matches.Where(m => m.GroupKey == null || !excludeGroupKeys.Contains(m.GroupKey));
        }

        return matches;
    }

    public async Task<PagedList<Guid>> QueryIdsAsync(
        LibraryImportCandidateQuery query,
        PagingParameter paging,
        CancellationToken cancellationToken)
    {
        try
        {
            var matches = ApplyCriteria(query);
            var totalCount = await matches.CountAsync(cancellationToken).ConfigureAwait(false);
            var ids = await matches
                .OrderBy(m => m.Id)
                .Skip((paging.PageNumber - 1) * paging.PageSize)
                .Take(paging.PageSize)
                .Select(m => m.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return new()
            {
                Items = ids,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalItems = totalCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The library import repository failed to query library import candidate ids.");
            throw;
        }
    }

    public async Task<PagedList<LibraryImportCandidateGroupSummary>> QueryGroupsAsync(
        LibraryImportCandidateQuery query,
        PagingParameter paging,
        CancellationToken cancellationToken)
    {
        try
        {
            var matches = ApplyCriteria(query).Where(m => m.GroupKey != null);

            var groupKeys = matches.Select(m => m.GroupKey!).Distinct();
            var totalCount = await groupKeys.CountAsync(cancellationToken).ConfigureAwait(false);
            var pageKeys = await groupKeys
                .OrderBy(groupKey => groupKey)
                .Skip((paging.PageNumber - 1) * paging.PageSize)
                .Take(paging.PageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var statusCounts = await matches
                .Where(m => pageKeys.Contains(m.GroupKey!))
                .GroupBy(m => new { m.GroupKey, m.Status })
                .Select(group => new { group.Key.GroupKey, group.Key.Status, Count = group.Count() })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var representatives = await matches
                .Where(m => pageKeys.Contains(m.GroupKey!) && m.Id == matches
                    .Where(other => other.GroupKey == m.GroupKey)
                    .OrderByDescending(other => other.MatchCount == 1)
                    .ThenBy(other => other.Id)
                    .Select(other => other.Id)
                    .First())
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var representativeByGroupKey = representatives.ToDictionary(m => m.GroupKey!, StringComparer.Ordinal);

            var items = new List<LibraryImportCandidateGroupSummary>(pageKeys.Count);
            foreach (var groupKey in pageKeys)
            {
                if (!representativeByGroupKey.TryGetValue(groupKey, out var representative))
                {
                    continue;
                }

                items.Add(new()
                {
                    GroupKey = groupKey,
                    Representative = representative,
                    StatusCounts = statusCounts
                        .Where(count => count.GroupKey == groupKey)
                        .ToDictionary(count => count.Status, count => count.Count)
                });
            }

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
            logger.LogError(ex, "The library import repository failed to query library import candidate groups.");
            throw;
        }
    }

    public async Task<PagedList<LibraryImportCandidate>> QueryAsync(
        LibraryImportCandidateQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var matches = ApplyCriteria(query);

            var totalCount = await matches.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortProperty = context.Model.FindEntityType(typeof(LibraryImportCandidate))!.GetProperties()
                .Where(property => !property.IsShadowProperty() &&
                    (property.ClrType.IsValueType || property.ClrType == typeof(string) ||
                        property.ClrType == typeof(Uri)))
                .Select(property => property.Name)
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                matches = sort.Direction == SortDirection.Ascending
                    ? matches.OrderBy(m => EF.Property<object>(m, sortProperty)).ThenBy(m => m.Id)
                    : matches.OrderByDescending(m => EF.Property<object>(m, sortProperty))
                        .ThenBy(m => m.Id);
            }
            else
            {
                matches = matches.OrderBy(m => m.Id);
            }

            matches = matches.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            var items = await matches.ToListAsync(cancellationToken).ConfigureAwait(false);

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
            logger.LogError(ex, "The library import repository failed to query library import matches.");
            throw;
        }
    }

    public async Task<bool> TryClaimForImportAsync(
        Guid candidateId,
        Guid? claimedByUserId,
        CancellationToken cancellationToken)
    {
        return await TransitionStatusAsync(
            candidateId,
            [LibraryImportCandidateStatus.Pending, LibraryImportCandidateStatus.Failed],
            LibraryImportCandidateStatus.Queued,
            note: null,
            claimedByUserId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryBeginImportAsync(Guid candidateId, Guid? userId, CancellationToken cancellationToken)
    {
        return await TransitionStatusAsync(
            candidateId,
            [LibraryImportCandidateStatus.Queued],
            LibraryImportCandidateStatus.Importing,
            note: null,
            userId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryResetImportAsync(
        Guid candidateId,
        string? note,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        return await TransitionStatusAsync(
            candidateId,
            [LibraryImportCandidateStatus.Queued, LibraryImportCandidateStatus.Importing],
            LibraryImportCandidateStatus.Pending,
            note,
            userId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> MarkMatchingFailedAsync(
        Guid candidateId,
        string reason,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        return await TransitionStatusAsync(
            candidateId,
            [LibraryImportCandidateStatus.MatchingPending],
            LibraryImportCandidateStatus.Failed,
            reason,
            userId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> MarkImportFailedAsync(
        Guid candidateId,
        string reason,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        return await TransitionStatusAsync(
            candidateId,
            [LibraryImportCandidateStatus.Queued, LibraryImportCandidateStatus.Importing],
            LibraryImportCandidateStatus.Failed,
            reason,
            userId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TryRejectAsync(Guid candidateId, Guid? userId, CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var candidate = await context.LibraryImportCandidates.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == candidateId, cancellationToken).ConfigureAwait(false);
            if (candidate is null)
            {
                return false;
            }

            var groupKey = ResolveGroupKey(candidate with { Matches = [] });
            List<LibraryImportMatch> noMatches = [];
            var affected = await context.LibraryImportCandidates
                .Where(m => m.Id == candidateId &&
                    (m.Status == LibraryImportCandidateStatus.Pending ||
                        m.Status == LibraryImportCandidateStatus.Failed ||
                        m.Status == LibraryImportCandidateStatus.MatchingPending))
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(m => m.Status, LibraryImportCandidateStatus.Rejected)
                        .SetProperty(m => m.Matches, noMatches)
                        .SetProperty(m => m.MatchCount, 0)
                        .SetProperty(m => m.ContentHierarchyItemId, (Guid?)null)
                        .SetProperty(m => m.GroupKey, groupKey)
                        .SetProperty(m => m.Notes, "The user rejected the matches of this file.")
                        .SetProperty(m => m.ModifiedAt, now)
                        .SetProperty(m => m.ModifiedByUserId, userId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (affected > 0)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ContentEventBuilder.BuildLibraryImportCandidateUpdatedEvent(candidateId),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return affected > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The library import repository failed to reject library import candidate {LibraryImportCandidateId}.",
                candidateId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryUpdateTargetAsync(
        Guid candidateId,
        Guid? contentHierarchyItemId,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var affected = await context.LibraryImportCandidates
                .Where(m => m.Id == candidateId &&
                    ((m.Status == LibraryImportCandidateStatus.Importing && contentHierarchyItemId != null) ||
                        (m.MatchCount == 1 &&
                            (m.Status == LibraryImportCandidateStatus.Pending ||
                                m.Status == LibraryImportCandidateStatus.Failed))))
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(m => m.ContentHierarchyItemId, contentHierarchyItemId)
                        .SetProperty(m => m.ModifiedAt, now)
                        .SetProperty(m => m.ModifiedByUserId, userId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (affected > 0)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ContentEventBuilder.BuildLibraryImportCandidateUpdatedEvent(candidateId),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return affected > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The library import repository failed to save the target of library import " +
                "candidate {LibraryImportCandidateId}.",
                candidateId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryRestoreAsync(Guid candidateId, Guid? userId, CancellationToken cancellationToken)
    {
        return await TransitionStatusAsync(
            candidateId,
            [LibraryImportCandidateStatus.Rejected],
            LibraryImportCandidateStatus.MatchingPending,
            note: null,
            userId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TransitionStatusAsync(
        Guid candidateId,
        IReadOnlyCollection<LibraryImportCandidateStatus> expected,
        LibraryImportCandidateStatus next,
        string? note,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var affected = await context.LibraryImportCandidates
                .Where(m => m.Id == candidateId && expected.Contains(m.Status))
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(m => m.Status, next)
                        .SetProperty(m => m.Notes, note)
                        .SetProperty(m => m.ModifiedAt, now)
                        .SetProperty(m => m.ModifiedByUserId, userId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (affected > 0)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ContentEventBuilder.BuildLibraryImportCandidateUpdatedEvent(candidateId),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return affected > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The library import repository failed to change library import candidate {LibraryImportCandidateId} " +
                "from {From} to {To}.",
                candidateId,
                expected,
                next);
            throw;
        }
    }

    public async Task<int> DeleteByRootFolderAsync(
        Guid rootFolderId,
        IReadOnlyCollection<Guid>? candidateIds,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var candidates = context.LibraryImportCandidates
                        .Where(m => m.RootFolderId == rootFolderId &&
                            m.Status != LibraryImportCandidateStatus.Queued &&
                            m.Status != LibraryImportCandidateStatus.Importing &&
                            m.Status != LibraryImportCandidateStatus.Rejected);
                    if (candidateIds is not null)
                    {
                        var ids = candidateIds as Guid[] ?? [.. candidateIds];
                        candidates = candidates.Where(m => EF.Parameter(ids).Contains(m.Id));
                    }

                    var deleted = await candidates
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (deleted > 0)
                    {
                        var candidateCountLabel = deleted == 1
                            ? "1 import candidate"
                            : $"{deleted} import candidates";
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Content,
                                Operation = "Delete",
                                EntityId = rootFolderId.ToString(),
                                EntityType = nameof(LibraryImportCandidate),
                                Detail = $"Deleted {candidateCountLabel} for root folder {rootFolderId}.",
                                Timestamp = now,
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return deleted;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The library import repository failed to delete library import matches for root folder {RootFolderId}.",
                rootFolderId);
            throw;
        }
    }

    public async Task<LibraryImportCandidate> CreateAsync(
        LibraryImportCandidate model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    model.CreatedAt = now;
                    model.CreatedByUserId = createdByUserId;

                    model.MatchCount = model.Matches.Count;
                    model.GroupKey = ResolveGroupKey(model);

                    context.LibraryImportCandidates.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Content,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(LibraryImportCandidate),
                            Detail = $"Created import candidate for {model.FilePath}.",
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
            logger.LogError(
                ex,
                "The library import repository failed to create library import candidate for {FilePath}.",
                model.FilePath);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<bool> TryResolveMatchAsync(
        LibraryImportCandidate model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken) =>
        UpdateAsync(model, modifiedByUserId, LibraryImportCandidateStatus.MatchingPending, cancellationToken);

    /// <inheritdoc />
    public Task<bool> UpdateAsync(
        LibraryImportCandidate model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken) =>
        UpdateAsync(model, modifiedByUserId, null, cancellationToken);

    private async Task<bool> UpdateAsync(
        LibraryImportCandidate model,
        Guid? modifiedByUserId,
        LibraryImportCandidateStatus? expectedStatus,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    model.GroupKey = ResolveGroupKey(model);

                    var affected = await context.LibraryImportCandidates
                        .Where(m => m.Id == model.Id && (expectedStatus == null || m.Status == expectedStatus))
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(m => m.RootFolderId, model.RootFolderId)
                                .SetProperty(m => m.ScanRunId, model.ScanRunId)
                                .SetProperty(m => m.FilePath, model.FilePath)
                                .SetProperty(m => m.ContentType, model.ContentType)
                                .SetProperty(m => m.Status, model.Status)
                                .SetProperty(m => m.ParsedSignals, model.ParsedSignals)
                                .SetProperty(m => m.Matches, model.Matches)
                                .SetProperty(m => m.MatchCount, model.Matches.Count)
                                .SetProperty(m => m.ContentHierarchyItemId, model.ContentHierarchyItemId)
                                .SetProperty(m => m.LayoutKey, model.LayoutKey)
                                .SetProperty(m => m.GroupKey, model.GroupKey)
                                .SetProperty(m => m.Notes, model.Notes)
                                .SetProperty(m => m.ModifiedAt, now)
                                .SetProperty(m => m.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (affected > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Content,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(LibraryImportCandidate),
                                Detail = $"Updated import candidate for {model.FilePath}.",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return affected > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (updated)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ContentEventBuilder.BuildLibraryImportCandidateUpdatedEvent(model.Id),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return updated;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The library import repository failed to update library import candidate {LibraryImportCandidateId}.",
                model.Id);
            throw;
        }
    }

    public async Task<BulkResult> BulkPatchAsync(
        BulkPatch<LibraryImportCandidateQuery, LibraryImportCandidatePatch> patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var patchedIds = new List<Guid>();
            var result = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    if (patch.Patch.Matches is not { } matchesPatch)
                    {
                        return new BulkResult { Count = 0 };
                    }

                    var now = _time.GetUtcNow().UtcDateTime;
                    var targets = new List<LibraryImportCandidate>();
                    var targetIds = new HashSet<Guid>();
                    if (patch.Ids is { Count: > 0 } ids)
                    {
                        var idList = ids as Guid[] ?? [.. ids];
                        var selected = await context.LibraryImportCandidates
                            .AsNoTracking()
                            .Where(m => idList.Contains(m.Id))
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);
                        targets.AddRange(selected.Where(candidate => targetIds.Add(candidate.Id)));
                    }

                    if (patch.Filter is not null)
                    {
                        var filtered = ApplyCriteria(patch.Filter).OrderBy(candidate => candidate.Id);
                        for (var pageNumber = 0; ; pageNumber++)
                        {
                            var page = await filtered
                                .Skip(pageNumber * BulkPageSize)
                                .Take(BulkPageSize)
                                .ToListAsync(cancellationToken)
                                .ConfigureAwait(false);
                            targets.AddRange(page.Where(candidate => targetIds.Add(candidate.Id)));
                            if (page.Count < BulkPageSize)
                            {
                                break;
                            }
                        }
                    }

                    if (targets.Count == 0)
                    {
                        return new BulkResult { Count = 0 };
                    }

                    var matches = matchesPatch.Value;
                    var affected = 0;

                    foreach (var group in targets.GroupBy(
                        candidate => ResolveGroupKey(candidate with { Matches = matches })))
                    {
                        var groupIds = group.Select(candidate => candidate.Id).ToArray();
                        affected += await context.LibraryImportCandidates
                            .Where(m => groupIds.Contains(m.Id) && m.Status != LibraryImportCandidateStatus.Rejected)
                            .ExecuteUpdateAsync(
                                s => s
                                    .SetProperty(m => m.Matches, matches)
                                    .SetProperty(m => m.MatchCount, matches.Count)
                                    .SetProperty(m => m.ContentHierarchyItemId, (Guid?)null)
                                    .SetProperty(m => m.Status, m =>
                                        m.Status == LibraryImportCandidateStatus.MatchingPending
                                        ? LibraryImportCandidateStatus.Pending : m.Status)
                                    .SetProperty(m => m.GroupKey, group.Key)
                                    .SetProperty(m => m.ModifiedAt, now)
                                    .SetProperty(m => m.ModifiedByUserId, modifiedByUserId),
                                cancellationToken)
                            .ConfigureAwait(false);
                        patchedIds.AddRange(groupIds);
                    }

                    if (affected > 0)
                    {
                        var candidateCountLabel = affected == 1
                            ? "1 import candidate"
                            : $"{affected} import candidates";
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Content,
                                Operation = "Update",
                                EntityType = nameof(LibraryImportCandidate),
                                Detail = $"Updated the matches of {candidateCountLabel}.",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return new BulkResult { Count = affected };
                },
                cancellationToken).ConfigureAwait(false);

            if (patchedIds.Count > 0)
            {
                await transactionManager.OnCommittedAsync(
                    async token =>
                    {
                        foreach (var id in patchedIds)
                        {
                            await eventBus.PublishAsync(
                                ContentEventBuilder.BuildLibraryImportCandidateUpdatedEvent(id),
                                token).ConfigureAwait(false);
                        }
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The library import repository failed to bulk-patch the library import candidates.");
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var removed = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var model = await (
                        from m in context.LibraryImportCandidates.AsNoTracking()
                        where m.Id == id
                        select new { m.FilePath })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var deleted = await context.LibraryImportCandidates
                        .Where(m => m.Id == id)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (deleted > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Content,
                                Operation = "Delete",
                                EntityId = id.ToString(),
                                EntityType = nameof(LibraryImportCandidate),
                                Detail = $"Deleted import candidate for {model.FilePath}.",
                                Timestamp = now,
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return deleted > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (removed)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ContentEventBuilder.BuildLibraryImportCandidateUpdatedEvent(id),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The library import repository failed to delete library import candidate {LibraryImportCandidateId}.",
                id);
            throw;
        }
    }
}
