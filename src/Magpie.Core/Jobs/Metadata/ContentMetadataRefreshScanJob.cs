using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Metadata;

internal sealed class ContentMetadataRefreshScanJob(
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IJobManager jobManager,
    ILogger<ContentMetadataRefreshScanJob> logger,
    IProgressReporter? progressReporter = null) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = "ContentMetadataRefreshScan",
        DisplayName = "Content metadata refresh scan",
        Description = "Finds content still using placeholder metadata and queues one metadata refresh per item.",
        Category = "Metadata",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Requeue,
        Timeout = TimeSpan.FromMinutes(5),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters = [],
    };

    private static double GetProgress(int completed, int total)
    {
        if (total == 0)
        {
            return 0.5;
        }

        return Math.Min(0.95, completed / (double)total);
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        var progress = progressReporter;
        progress?.Report(0, "The scan will load the placeholder metadata items.");

        const int pageSize = 500;
        var placeholderIds = new List<Guid>();
        var pageNumber = 1;
        while (true)
        {
            var page = await contentHierarchyItemRepository.QueryAsync(
                new ContentHierarchyItemCriteria { PendingMetadataRefresh = true },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            placeholderIds.AddRange(pageItems.Select(item => item.Id));
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        if (placeholderIds.Count == 0)
        {
            progress?.Report(1, "The content metadata refresh scan completed because no item needs a refresh.");
            return;
        }

        var queued = 0;
        for (var i = 0; i < placeholderIds.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(GetProgress(i, placeholderIds.Count), null);

            var itemId = placeholderIds[i];
            try
            {
                await jobManager.ExecuteAsync(
                    new()
                    {
                        JobType = ContentMetadataRefreshItemJob.Spec.JobType,
                        Parameters = new Dictionary<string, string>()
                        {
                            ["contentHierarchyItemId"] = itemId.ToString("D"),
                        },
                        Mode = JobExecutionMode.Queue,
                        UserId = context.ManualTriggerUserId,
                        CorrelationId = context.CorrelationId,
                    },
                    cancellationToken).ConfigureAwait(false);
                queued++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The metadata refresh scan failed to enqueue a refresh for item {ContentHierarchyItemId}",
                    itemId);
            }
        }

        progress?.Report(
            1,
            $"The content metadata refresh scan completed and queued {queued} of {placeholderIds.Count} items.");
    }
}
