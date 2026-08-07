using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

namespace Magpie.Core;

internal sealed class AuditHistoryService(
    IAuditRecordRepository auditRecords,
    IJobExecutionRepository jobExecutions,
    IDownloadRepository downloads) : IAuditHistoryService
{
    private static readonly string DownloadIdTriggerContextKey = MagpieEvents.Keys.DownloadId.ToEventContextKey();

    private static readonly string[] EntityParameterKeys = ["contentHierarchyItemId", "contentItemId"];

    private const string JobCategory = "Job";
    private const string DownloadIdParameterKey = "downloadId";

    private static AuditHistoryEntry ToEntry(JobExecution job) => new()
    {
        Id = job.Id,
        Kind = AuditHistoryEntryKind.JobExecution,
        Timestamp = job.CompletedAt ?? job.StartedAt ?? job.CreatedAt,
        CorrelationId = job.CorrelationId,
        Label = job.JobType,
        Category = JobCategory,
        Status = job.Status.ToString(),
        Detail = job.StatusMessage,
        EntityId = ResolveEntityId(job),
        UserId = job.CreatedByUserId
    };

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
        UserId = audit.UserId
    };

    private static bool WithinRange(DateTime timestamp, DateTime? from, DateTime? to)
        => (from is null || timestamp >= from.Value) && (to is null || timestamp <= to.Value);

    private static bool ReferencesContent(JobExecution job, ContentActivityScope contentScope)
        => ReferencesContent(job.Parameters, DownloadIdParameterKey, contentScope) ||
            ReferencesContent(job.TriggerContext, DownloadIdTriggerContextKey, contentScope);

    private static bool ReferencesContent(
        IReadOnlyDictionary<string, string> values,
        string downloadIdKey,
        ContentActivityScope contentScope)
    {
        foreach (var key in EntityParameterKeys)
        {
            if (values.TryGetValue(key, out var value) &&
                string.Equals(value, contentScope.ContentId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return values.TryGetValue(downloadIdKey, out var downloadId) && contentScope.DownloadIds.Contains(downloadId);
    }

    private static string? ResolveEntityId(JobExecution job)
        => ResolveEntityId(job.Parameters, DownloadIdParameterKey)
            ?? ResolveEntityId(job.TriggerContext, DownloadIdTriggerContextKey);

    private static string? ResolveEntityId(IReadOnlyDictionary<string, string> values, string downloadIdKey)
    {
        foreach (var key in EntityParameterKeys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return values.GetValueOrDefault(downloadIdKey);
    }

    public async Task<PagedList<AuditHistoryEntry>> QueryAsync(
        AuditHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var includeJobs = query.Kind is null or AuditHistoryEntryKind.JobExecution;
        var includeAudits = query.Kind is null or AuditHistoryEntryKind.AuditRecord;

        JobExecutionStatus? statusFilter = null;
        if (query.Status is { Length: > 0 } statusText)
        {
            includeAudits = false;
            if (Enum.TryParse<JobExecutionStatus>(statusText, ignoreCase: true, out var parsedStatus))
            {
                statusFilter = parsedStatus;
            }
            else
            {
                includeJobs = false;
            }
        }

        var contentScope = includeJobs
            ? await ResolveContentScopeAsync(query.ContentId, query.Paging.PageSize, cancellationToken)
                .ConfigureAwait(false)
            : null;

        var entries = new List<AuditHistoryEntry>();

        if (includeJobs)
        {
            var jobs = await QueryJobsAsync(
                contentScope,
                statusFilter,
                query.From,
                query.To,
                query.Paging.PageSize,
                cancellationToken).ConfigureAwait(false);
            entries.AddRange(jobs
                .Where(job => contentScope is null || ReferencesContent(job, contentScope))
                .Select(ToEntry));
        }

        if (includeAudits)
        {
            var audits = await QueryAuditsAsync(
                query.ContentId?.ToString(),
                query.From,
                query.To,
                query.Paging.PageSize,
                cancellationToken).ConfigureAwait(false);
            entries.AddRange(audits.Select(ToEntry));
        }

        var ordered = entries
            .Where(entry => WithinRange(entry.Timestamp, query.From, query.To))
            .OrderByDescending(entry => entry.Timestamp)
            .ToList();

        var pageItems = ordered
            .Skip((query.Paging.PageNumber - 1) * query.Paging.PageSize)
            .Take(query.Paging.PageSize)
            .ToList();

        return new PagedList<AuditHistoryEntry>
        {
            Items = pageItems,
            PageNumber = query.Paging.PageNumber,
            PageSize = query.Paging.PageSize,
            TotalItems = ordered.Count
        };
    }

    private async Task<ContentActivityScope?> ResolveContentScopeAsync(
        Guid? contentId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (contentId is not { } resolvedContentId)
        {
            return null;
        }

        var downloadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var pageNumber = 1; ; pageNumber++)
        {
            var page = await downloads.QueryAsync(
                new DownloadQuery { ContentHierarchyItemIds = [resolvedContentId] },
                new PagingParameter { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            foreach (var download in pageItems)
            {
                downloadIds.Add(download.Id.ToString("D"));
            }

            if (pageItems.Count < pageSize)
            {
                break;
            }
        }

        return new ContentActivityScope(resolvedContentId.ToString("D"), downloadIds);
    }

    private async Task<IReadOnlyList<JobExecution>> QueryJobsAsync(
        ContentActivityScope? contentScope,
        JobExecutionStatus? status,
        DateTime? from,
        DateTime? to,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var jobQuery = new JobExecutionQuery
        {
            Status = status,
            ParameterValueReferences = contentScope?.EntityIds,
            ExecutionTimestamp = from is null && to is null ? null : new(from, to)
        };

        var jobs = new List<JobExecution>();
        for (var pageNumber = 1; ; pageNumber++)
        {
            var page = await jobExecutions.QueryAsync(
                jobQuery,
                new PagingParameter { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            jobs.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }
        }

        return jobs;
    }

    private async Task<IReadOnlyList<AuditRecord>> QueryAuditsAsync(
        string? contentId,
        DateTime? from,
        DateTime? to,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var auditQuery = new AuditRecordQuery
        {
            EntityId = contentId,
            Timestamp = from is null && to is null ? null : new(from, to)
        };

        var audits = new List<AuditRecord>();
        for (var pageNumber = 1; ; pageNumber++)
        {
            var page = await auditRecords.QueryAsync(
                auditQuery,
                new PagingParameter { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            audits.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }
        }

        return audits;
    }

    private sealed record ContentActivityScope(string ContentId, IReadOnlySet<string> DownloadIds)
    {
        public IReadOnlyCollection<string> EntityIds => [ContentId, .. DownloadIds];
    }
}
