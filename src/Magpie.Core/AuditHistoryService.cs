using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

namespace Magpie.Core;

internal sealed class AuditHistoryService(
    IAuditHistoryRepository auditHistory,
    IAuditRecordRepository auditRecords,
    IJobExecutionRepository jobExecutions,
    IDownloadRepository downloads,
    ITransactionManager transactionManager,
    TimeProvider timeProvider) : IAuditHistoryService
{
    private static readonly int[] PurgeAgesInDays = [7, 30, 90, 365];

    /// <inheritdoc />
    public async Task<PagedList<AuditHistoryEntry>> QueryAsync(
        AuditHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var references = query.ContentId is { } contentId && query.Kind != AuditHistoryEntryKind.AuditRecord
            ? await ResolveContentReferencesAsync(contentId, query.Paging.PageSize, cancellationToken)
                .ConfigureAwait(false)
            : null;

        return await auditHistory.QueryAsync(query, references, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AuditHistoryPurgeCounts> PurgeAsync(
        int olderThanDays,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        var cutoff = ResolvePurgeCutoff(olderThanDays);

        return await transactionManager.ExecuteWithTransactionAsync(
            async token =>
            {
                var counts = new AuditHistoryPurgeCounts
                {
                    JobExecutions = await jobExecutions
                        .DeleteWhereAsync(BuildJobExecutionPurgeQuery(cutoff), token)
                        .ConfigureAwait(false),
                    AuditRecords = await auditRecords
                        .DeleteWhereAsync(BuildAuditRecordPurgeQuery(cutoff), token)
                        .ConfigureAwait(false)
                };

                await auditRecords.CreateAsync(
                    new AuditRecord
                    {
                        Category = AuditCategories.System,
                        Operation = "ActivityLogPurged",
                        Detail = string.Format(
                            CultureInfo.InvariantCulture,
                            "Purged {0} events and {1} job runs from before {2:MMM d, yyyy}.",
                            counts.AuditRecords,
                            counts.JobExecutions,
                            cutoff),
                        UserId = callerUserId
                    },
                    token).ConfigureAwait(false);

                return counts;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static AuditRecordQuery BuildAuditRecordPurgeQuery(DateTime cutoff) => new() { TimestampBefore = cutoff };

    private static JobExecutionQuery BuildJobExecutionPurgeQuery(DateTime cutoff) => new()
    {
        Statuses = [JobExecutionStatus.Completed, JobExecutionStatus.Failed, JobExecutionStatus.Cancelled],
        CompletedAt = new(null, cutoff)
    };

    private DateTime ResolvePurgeCutoff(int olderThanDays)
    {
        if (!PurgeAgesInDays.Contains(olderThanDays))
        {
            throw new ArgumentOutOfRangeException(
                nameof(olderThanDays),
                olderThanDays,
                "The purge age is not 7, 30, 90 or 365 days.");
        }

        return timeProvider.GetUtcNow().UtcDateTime.AddDays(-olderThanDays);
    }

    private async Task<IReadOnlyCollection<string>> ResolveContentReferencesAsync(
        Guid contentId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { contentId.ToString("D") };
        for (var pageNumber = 1; ; pageNumber++)
        {
            var page = await downloads.QueryAsync(
                new DownloadQuery { ContentHierarchyItemIds = [contentId] },
                new PagingParameter { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            foreach (var download in pageItems)
            {
                references.Add(download.Id.ToString("D"));
            }

            if (pageItems.Count < pageSize)
            {
                return references;
            }
        }
    }
}
