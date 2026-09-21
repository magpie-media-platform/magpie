using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
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

    private static readonly string[] EntityParameterKeys =
        [MagpieJobs.Parameters.ContentHierarchyItemId, MagpieJobs.Parameters.ContentItemId];

    private static readonly SortParameter ExecutionTimestampDescending = new()
    {
        PropertyName = nameof(JobExecutionQuery.ExecutionTimestamp),
        Direction = SortDirection.Descending
    };

    private const string JobCategory = "Job";

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
        UserId = audit.UserId,
        JobExecutionId = audit.JobExecutionId
    };

    private static string? ResolveEntityId(JobExecution job)
        => ResolveEntityId(job.Parameters, MagpieJobs.Parameters.DownloadId)
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
        var includeJobs = (query.Kind is null or AuditHistoryEntryKind.JobExecution) && query.JobExecutionId is null;
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

        var reach = (long)query.Paging.PageNumber * query.Paging.PageSize;
        var merged = new PagingParameter
        {
            PageNumber = 1,
            PageSize = (int)Math.Min(reach, int.MaxValue)
        };
        if (merged.PageSize < reach)
        {
            throw new ArgumentException("The requested audit history page and page size exceed the supported range.");
        }

        var contentScope = includeJobs
            ? await ResolveContentScopeAsync(query.ContentId, query.Paging.PageSize, cancellationToken)
                .ConfigureAwait(false)
            : null;

        var timestampRange = query.From is null && query.To is null ? null : new Range<DateTime>(query.From, query.To);

        var entries = new List<AuditHistoryEntry>();
        var totalItems = 0;

        if (includeJobs)
        {
            var jobs = await jobExecutions.QueryAsync(
                new JobExecutionQuery
                {
                    Status = statusFilter,
                    ParameterValueReferences = contentScope?.EntityIds,
                    ExecutionTimestamp = timestampRange
                },
                merged,
                ExecutionTimestampDescending,
                cancellationToken).ConfigureAwait(false);
            entries.AddRange(jobs.Items.Select(ToEntry));
            totalItems += jobs.TotalItems;
        }

        if (includeAudits)
        {
            var audits = await auditRecords.QueryAsync(
                new AuditRecordQuery
                {
                    EntityId = query.ContentId?.ToString(),
                    JobExecutionId = query.JobExecutionId,
                    Timestamp = timestampRange
                },
                merged,
                sort: null,
                cancellationToken).ConfigureAwait(false);
            entries.AddRange(audits.Items.Select(ToEntry));
            totalItems += audits.TotalItems;
        }

        var pageItems = entries
            .OrderByDescending(entry => entry.Timestamp)
            .Skip((query.Paging.PageNumber - 1) * query.Paging.PageSize)
            .Take(query.Paging.PageSize)
            .ToList();

        return new PagedList<AuditHistoryEntry>
        {
            Items = pageItems,
            PageNumber = query.Paging.PageNumber,
            PageSize = query.Paging.PageSize,
            TotalItems = totalItems
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

    private sealed record ContentActivityScope(string ContentId, IReadOnlySet<string> DownloadIds)
    {
        public IReadOnlyCollection<string> EntityIds => [ContentId, .. DownloadIds];
    }
}
