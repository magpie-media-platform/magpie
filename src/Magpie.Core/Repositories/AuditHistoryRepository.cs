using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Data;
using Magpie.Data.Configuration;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Events;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class AuditHistoryRepository(
    MagpieDbContext context,
    IJobRegistry jobRegistry,
    ILogger<AuditHistoryRepository> logger) : IAuditHistoryRepository
{
    private const string SystemUser = "system";

    private static readonly string DownloadIdTriggerContextKey = MagpieEvents.Keys.DownloadId.ToEventContextKey();

    private static readonly (string Key, string EntityType)[] EntityParameterKeys =
    [
        (MagpieJobs.Parameters.ContentHierarchyItemId, nameof(ContentHierarchyItem)),
        (MagpieJobs.Parameters.ContentItemId, nameof(ContentItem))
    ];

    private static AuditHistoryEntry ToEntry(JobExecution job)
    {
        var target = ResolveTarget(job.Parameters, MagpieJobs.Parameters.DownloadId)
            ?? ResolveTarget(job.TriggerContext, DownloadIdTriggerContextKey);
        return new()
        {
            Id = job.Id,
            Kind = AuditHistoryEntryKind.JobExecution,
            Timestamp = job.CompletedAt ?? job.StartedAt ?? job.CreatedAt,
            CorrelationId = job.CorrelationId,
            Label = job.JobType,
            Category = AuditCategories.Jobs,
            Status = job.Status.ToString(),
            Detail = job.StatusMessage,
            EntityId = target?.Id,
            EntityType = target?.EntityType,
            UserId = job.CreatedByUserId
        };
    }

    private static AuditHistoryEntry ToEntry(AuditRecord audit) => new()
    {
        Id = audit.Id,
        Kind = AuditHistoryEntryKind.AuditRecord,
        Timestamp = audit.Timestamp,
        CorrelationId = audit.CorrelationId,
        Label = audit.Operation,
        Category = audit.Category,
        Detail = audit.Detail,
        EntityId = audit.EntityId,
        EntityType = audit.EntityType,
        UserId = audit.UserId,
        JobExecutionId = audit.JobExecutionId
    };

    private static (string Id, string EntityType)? ResolveTarget(
        IReadOnlyDictionary<string, string> values,
        string downloadIdKey)
    {
        foreach (var (key, entityType) in EntityParameterKeys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            {
                return (value, entityType);
            }
        }

        return values.TryGetValue(downloadIdKey, out var downloadId) && !string.IsNullOrEmpty(downloadId)
            ? (downloadId, nameof(Download))
            : null;
    }

    private static string NormalizeReferenceValue(string value)
    {
        var bounded = value.Length > JobExecutionReferenceColumns.MaxLength
            ? value[..JobExecutionReferenceColumns.MaxLength]
            : value;
        return bounded.ToLowerInvariant();
    }

    /// <inheritdoc />
    public async Task<PagedList<AuditHistoryEntry>> QueryAsync(
        AuditHistoryQuery query,
        IReadOnlyCollection<string>? parameterValueReferences,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var systemUserIds = query.HasUser is null
                ? new List<Guid?>()
                : await context.Users
                    .AsNoTracking()
                    .Where(user => user.Username == SystemUser && user.ProviderType == SystemUser)
                    .Select(user => (Guid?)user.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
            var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim().ToLowerInvariant();
            var nameSearch = search is null
                ? null
                : string.Concat(search.Where(character => !char.IsWhiteSpace(character)));
            var jobExecutions = FilterJobExecutions(query, parameterValueReferences, systemUserIds, search, nameSearch);
            var auditRecords = FilterAuditRecords(query, systemUserIds, search, nameSearch);
            var jobKeys = jobExecutions is null
                ? null
                : SelectJobKeys(
                    jobExecutions,
                    query.From,
                    query.To,
                    query.Status is null or "" &&
                        parameterValueReferences is null &&
                        query.CorrelationId is null &&
                        search is null);
            var auditKeys = auditRecords is null
                ? null
                : InRange(
                    auditRecords.Select(record => new AuditHistoryKey
                    {
                        Id = record.Id,
                        Kind = AuditHistoryEntryKind.AuditRecord,
                        Timestamp = record.Timestamp
                    }),
                    query.From,
                    query.To);
            var keys = jobKeys is null ? auditKeys : auditKeys is null ? jobKeys : jobKeys.Concat(auditKeys);

            var totalItems = keys is null ? 0 : await keys.CountAsync(cancellationToken).ConfigureAwait(false);
            var offset = (long)(query.Paging.PageNumber - 1) * query.Paging.PageSize;
            var pageKeys = keys is null || offset >= totalItems
                ? []
                : await ReadPageKeysAsync(
                    keys,
                    query.Direction,
                    offset,
                    query.Paging.PageSize,
                    totalItems,
                    cancellationToken).ConfigureAwait(false);

            var jobIds = pageKeys
                .Where(key => key.Kind == AuditHistoryEntryKind.JobExecution)
                .Select(key => key.Id)
                .ToList();
            var auditIds = pageKeys
                .Where(key => key.Kind == AuditHistoryEntryKind.AuditRecord)
                .Select(key => key.Id)
                .ToList();
            var jobs = await context.JobExecutions
                .AsNoTracking()
                .Where(execution => jobIds.Contains(execution.Id))
                .ToDictionaryAsync(execution => execution.Id, cancellationToken)
                .ConfigureAwait(false);
            var audits = await context.AuditRecords
                .AsNoTracking()
                .Where(record => auditIds.Contains(record.Id))
                .ToDictionaryAsync(record => record.Id, cancellationToken)
                .ConfigureAwait(false);

            var entries = new List<AuditHistoryEntry>(pageKeys.Count);
            foreach (var key in pageKeys)
            {
                if (key.Kind == AuditHistoryEntryKind.JobExecution && jobs.TryGetValue(key.Id, out var job))
                {
                    entries.Add(ToEntry(job));
                }
                else if (key.Kind == AuditHistoryEntryKind.AuditRecord && audits.TryGetValue(key.Id, out var audit))
                {
                    entries.Add(ToEntry(audit));
                }
            }

            entries = await ResolveContentItemTargetsAsync(entries, cancellationToken).ConfigureAwait(false);
            var names = await ReadTargetNamesAsync(entries, cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = entries
                    .Select(entry => entry.EntityType is { } entityType &&
                        Guid.TryParse(entry.EntityId, out var entityId) &&
                        names.TryGetValue((entityType, entityId), out var name)
                            ? entry with { EntityName = name }
                            : entry)
                    .ToList(),
                PageNumber = query.Paging.PageNumber,
                PageSize = query.Paging.PageSize,
                TotalItems = totalItems
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The audit history repository failed to query the audit history.");
            throw;
        }
    }

    private static IQueryable<AuditHistoryKey> SelectJobKeys(
        IQueryable<JobExecution> jobExecutions,
        DateTime? from,
        DateTime? to,
        bool indexOrdered)
    {
        if (!indexOrdered)
        {
            return InRange(
                jobExecutions.Select(execution => new AuditHistoryKey
                {
                    Id = execution.Id,
                    Kind = AuditHistoryEntryKind.JobExecution,
                    Timestamp = execution.CompletedAt ?? execution.StartedAt ?? execution.CreatedAt
                }),
                from,
                to);
        }

        var finished = jobExecutions
            .Where(execution => execution.CompletedAt != null)
            .Select(execution => new AuditHistoryKey
            {
                Id = execution.Id,
                Kind = AuditHistoryEntryKind.JobExecution,
                Timestamp = execution.CompletedAt!.Value
            });
        var running = jobExecutions
            .Where(execution => execution.CompletedAt == null && execution.StartedAt != null)
            .Select(execution => new AuditHistoryKey
            {
                Id = execution.Id,
                Kind = AuditHistoryEntryKind.JobExecution,
                Timestamp = execution.StartedAt!.Value
            });
        var queued = jobExecutions
            .Where(execution => execution.CompletedAt == null && execution.StartedAt == null)
            .Select(execution => new AuditHistoryKey
            {
                Id = execution.Id,
                Kind = AuditHistoryEntryKind.JobExecution,
                Timestamp = execution.CreatedAt
            });

        return InRange(finished, from, to).Concat(InRange(running, from, to)).Concat(InRange(queued, from, to));
    }

    private static IQueryable<AuditHistoryKey> InRange(IQueryable<AuditHistoryKey> keys, DateTime? from, DateTime? to)
    {
        if (from is { } lowerBound)
        {
            keys = keys.Where(key => key.Timestamp >= lowerBound);
        }

        if (to is { } upperBound)
        {
            keys = keys.Where(key => key.Timestamp <= upperBound);
        }

        return keys;
    }

    private static async Task<List<AuditHistoryKey>> ReadPageKeysAsync(
        IQueryable<AuditHistoryKey> keys,
        SortDirection direction,
        long offset,
        int pageSize,
        int totalItems,
        CancellationToken cancellationToken)
    {
        var take = (int)Math.Min(pageSize, totalItems - offset);
        var offsetFromEnd = totalItems - offset - take;
        var fromEnd = offsetFromEnd < offset;
        var ascending = (direction == SortDirection.Ascending) != fromEnd;
        var ordered = ascending
            ? keys.OrderBy(key => key.Timestamp).ThenBy(key => key.Id)
            : keys.OrderByDescending(key => key.Timestamp).ThenByDescending(key => key.Id);

        var pageKeys = await ordered
            .Skip((int)(fromEnd ? offsetFromEnd : offset))
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (fromEnd)
        {
            pageKeys.Reverse();
        }

        return pageKeys;
    }

    private IQueryable<JobExecution>? FilterJobExecutions(
        AuditHistoryQuery query,
        IReadOnlyCollection<string>? parameterValueReferences,
        List<Guid?> systemUserIds,
        string? search,
        string? nameSearch)
    {
        if (query.Kind == AuditHistoryEntryKind.AuditRecord ||
            query.JobExecutionId is not null ||
            (query.Categories is { Count: > 0 } categories && !categories.Contains(AuditCategories.Jobs)))
        {
            return null;
        }

        var jobExecutions = context.JobExecutions.AsNoTracking();

        if (query.Status is { Length: > 0 } statusText)
        {
            if (!Enum.TryParse<JobExecutionStatus>(statusText, ignoreCase: true, out var status))
            {
                return null;
            }

            jobExecutions = jobExecutions.Where(execution => execution.Status == status);
        }

        if (parameterValueReferences is not null)
        {
            jobExecutions = FilterByReferences(jobExecutions, parameterValueReferences);
        }

        if (query.UserId is { } userId)
        {
            jobExecutions = jobExecutions.Where(execution => execution.CreatedByUserId == userId);
        }

        if (query.HasUser is { } hasUser)
        {
            jobExecutions = hasUser
                ? jobExecutions.Where(execution =>
                    execution.CreatedByUserId != null && !systemUserIds.Contains(execution.CreatedByUserId))
                : jobExecutions.Where(execution =>
                    execution.CreatedByUserId == null || systemUserIds.Contains(execution.CreatedByUserId));
        }

        if (query.CorrelationId is { } correlationId)
        {
            jobExecutions = jobExecutions.Where(execution => execution.CorrelationId == correlationId);
        }

        if (search is not null && nameSearch is not null)
        {
            jobExecutions = jobExecutions.Where(execution =>
                execution.JobType.ToLower().Contains(nameSearch) ||
                (execution.StatusMessage != null && execution.StatusMessage.ToLower().Contains(search)));
        }

        return jobExecutions;
    }

    private IQueryable<AuditRecord>? FilterAuditRecords(
        AuditHistoryQuery query,
        List<Guid?> systemUserIds,
        string? search,
        string? nameSearch)
    {
        if (query.Kind == AuditHistoryEntryKind.JobExecution || query.Status is { Length: > 0 })
        {
            return null;
        }

        var auditRecords = context.AuditRecords.AsNoTracking();

        if (query.ContentId is { } contentId)
        {
            var entityId = contentId.ToString();
            auditRecords = auditRecords.Where(record => record.EntityId == entityId);
        }

        if (query.JobExecutionId is { } jobExecutionId)
        {
            auditRecords = auditRecords.Where(record => record.JobExecutionId == jobExecutionId);
        }

        if (query.Categories is { Count: > 0 } categories)
        {
            var values = categories.ToArray();
            auditRecords = auditRecords.Where(record => values.Contains(record.Category));
        }

        if (query.UserId is { } userId)
        {
            auditRecords = auditRecords.Where(record => record.UserId == userId);
        }

        if (query.HasUser is { } hasUser)
        {
            auditRecords = hasUser
                ? auditRecords.Where(record => record.UserId != null && !systemUserIds.Contains(record.UserId))
                : auditRecords.Where(record => record.UserId == null || systemUserIds.Contains(record.UserId));
        }

        if (query.CorrelationId is { } correlationId)
        {
            auditRecords = auditRecords.Where(record => record.CorrelationId == correlationId);
        }

        if (search is not null && nameSearch is not null)
        {
            auditRecords = auditRecords.Where(record =>
                record.Operation.ToLower().Contains(nameSearch) ||
                (record.Detail != null && record.Detail.ToLower().Contains(search)));
        }

        return auditRecords;
    }

    private async Task<List<AuditHistoryEntry>> ResolveContentItemTargetsAsync(
        List<AuditHistoryEntry> entries,
        CancellationToken cancellationToken)
    {
        var contentItemIds = entries
            .Where(entry => entry.EntityType == nameof(ContentItem))
            .Select(entry => Guid.TryParse(entry.EntityId, out var id) ? id : (Guid?)null)
            .OfType<Guid>()
            .Distinct()
            .ToArray();
        if (contentItemIds.Length == 0)
        {
            return entries;
        }

        var hierarchyItemIds = await context.ContentItems
            .AsNoTracking()
            .Where(item => contentItemIds.Contains(item.Id))
            .Select(item => new { item.Id, item.ContentHierarchyItemId })
            .ToDictionaryAsync(item => item.Id, item => item.ContentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. entries.Select(entry => entry.EntityType == nameof(ContentItem) &&
                Guid.TryParse(entry.EntityId, out var contentItemId) &&
                hierarchyItemIds.TryGetValue(contentItemId, out var hierarchyItemId)
                    ? entry with
                    {
                        EntityId = hierarchyItemId.ToString("D"),
                        EntityType = nameof(ContentHierarchyItem)
                    }
                    : entry)
        ];
    }

    private async Task<Dictionary<(string EntityType, Guid Id), string>> ReadTargetNamesAsync(
        IReadOnlyCollection<AuditHistoryEntry> entries,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<(string EntityType, Guid Id), string>();
        var targets = entries
            .Where(entry => entry.EntityType is not null)
            .GroupBy(entry => entry.EntityType!, StringComparer.Ordinal);
        foreach (var target in targets)
        {
            var ids = target
                .Select(entry => Guid.TryParse(entry.EntityId, out var id) ? id : (Guid?)null)
                .OfType<Guid>()
                .Distinct()
                .ToArray();
            if (ids.Length == 0 || QueryTargetNames(target.Key, ids) is not { } targetNames)
            {
                continue;
            }

            foreach (var targetName in await targetNames.ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                names[(target.Key, targetName.Id)] = targetName.Name;
            }
        }

        return names;
    }

    private IQueryable<TargetName>? QueryTargetNames(string entityType, Guid[] ids) => entityType switch
    {
        nameof(ContentHierarchyItem) => context.ContentHierarchyItems
            .AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .Select(item => new TargetName { Id = item.Id, Name = item.Name }),
        nameof(JobExecution) => context.JobExecutions
            .AsNoTracking()
            .Where(execution => ids.Contains(execution.Id))
            .Select(execution => new TargetName { Id = execution.Id, Name = execution.JobType }),
        nameof(User) => context.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .Select(user => new TargetName { Id = user.Id, Name = user.Username }),
        nameof(Extension) => context.Extensions
            .AsNoTracking()
            .Where(extension => ids.Contains(extension.Id))
            .Select(extension => new TargetName { Id = extension.Id, Name = extension.Name }),
        nameof(Wishlist) => context.Wishlists
            .AsNoTracking()
            .Where(wishlist => ids.Contains(wishlist.Id))
            .Select(wishlist => new TargetName { Id = wishlist.Id, Name = wishlist.Name }),
        nameof(QualityTierList) => context.QualityTierLists
            .AsNoTracking()
            .Where(tierList => ids.Contains(tierList.Id))
            .Select(tierList => new TargetName { Id = tierList.Id, Name = tierList.Name }),
        nameof(ApiKey) => context.ApiKeys
            .AsNoTracking()
            .Where(apiKey => ids.Contains(apiKey.Id))
            .Select(apiKey => new TargetName { Id = apiKey.Id, Name = apiKey.Name }),
        nameof(RefreshToken) => context.RefreshTokens
            .AsNoTracking()
            .Where(token => ids.Contains(token.Id))
            .Join(
                context.Users,
                token => token.UserId,
                user => user.Id,
                (token, user) => new TargetName { Id = token.Id, Name = user.Username }),
        nameof(Download) => context.Downloads
            .AsNoTracking()
            .Where(download => ids.Contains(download.Id))
            .Select(download => new TargetName { Id = download.Id, Name = download.Title }),
        _ => null
    };

    private IQueryable<JobExecution> FilterByReferences(
        IQueryable<JobExecution> jobExecutions,
        IReadOnlyCollection<string> references)
    {
        var distinct = references
            .Select(NormalizeReferenceValue)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var hierarchyItemTargetTypes = ListEventTargetJobTypes(MagpieEvents.Keys.ContentHierarchyItemId);
        var contentItemTargetTypes = ListEventTargetJobTypes(MagpieEvents.Keys.ContentItemId);
        var downloadTargetTypes = ListEventTargetJobTypes(MagpieEvents.Keys.DownloadId);

        return jobExecutions.Where(execution =>
            distinct.Contains(EF.Property<string>(
                execution,
                JobExecutionReferenceColumns.ParameterContentHierarchyItemId)) ||
            distinct.Contains(EF.Property<string>(execution, JobExecutionReferenceColumns.ParameterContentItemId)) ||
            distinct.Contains(EF.Property<string>(execution, JobExecutionReferenceColumns.ParameterDownloadId)) ||
            (hierarchyItemTargetTypes.Contains(execution.JobType) &&
                distinct.Contains(EF.Property<string>(
                    execution,
                    JobExecutionReferenceColumns.TriggerContextContentHierarchyItemId))) ||
            (contentItemTargetTypes.Contains(execution.JobType) &&
                distinct.Contains(EF.Property<string>(
                    execution,
                    JobExecutionReferenceColumns.TriggerContextContentItemId))) ||
            (downloadTargetTypes.Contains(execution.JobType) &&
                distinct.Contains(EF.Property<string>(
                    execution,
                    JobExecutionReferenceColumns.TriggerContextDownloadId))));
    }

    private string[] ListEventTargetJobTypes(string eventContextKey) =>
    [
        .. jobRegistry.ListJobSpecs()
            .Where(spec => spec.LockPartitionEventContextKeys.Values.Contains(eventContextKey))
            .Select(spec => spec.JobType)
    ];

    private sealed class AuditHistoryKey
    {
        public Guid Id { get; init; }

        public AuditHistoryEntryKind Kind { get; init; }

        public DateTime Timestamp { get; init; }
    }

    private sealed class TargetName
    {
        public Guid Id { get; init; }

        public required string Name { get; init; }
    }
}
