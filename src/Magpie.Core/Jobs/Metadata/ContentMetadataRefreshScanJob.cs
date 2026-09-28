using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Jobs;
using Magpie.Sdk.Content;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Metadata;

internal sealed class ContentMetadataRefreshScanJob(
    IEnumerable<IContentHierarchyMetadataProvider> metadataProviders,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IJobManager jobManager,
    TimeProvider timeProvider,
    ILogger<ContentMetadataRefreshScanJob> logger,
    IProgressReporter progressReporter) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.ContentMetadataRefreshScan,
        DisplayName = "Content metadata refresh scan",
        Description = "Finds content still using placeholder metadata and queues one metadata refresh per item.",
        Category = "Metadata",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Requeue,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters = [],
    };

    private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(24);

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
        progressReporter.Report(0, "The scan will load the placeholder metadata items.");

        const int pageSize = 500;

        var cutoff = timeProvider.GetUtcNow().UtcDateTime - RetryInterval;

        var supportedContentTypes = metadataProviders
            .SelectMany(provider => provider.SupportedContentTypes)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (supportedContentTypes.Count == 0)
        {
            progressReporter.Report(
                1,
                "The content metadata refresh scan completed because no metadata provider is enabled.");
            return;
        }

        var contentTypeCriteria = supportedContentTypes.Contains("*", StringComparer.Ordinal)
            ? null
            : new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.In,
                        Values = supportedContentTypes
                    }
                ]
            };
        var placeholderIds = new List<Guid>();
        var pageNumber = 1;
        while (true)
        {
            var page = await contentHierarchyItemRepository.QueryIdsAsync(
                new ContentHierarchyItemCriteria
                {
                    PendingMetadataRefresh = true,
                    LastRefreshedBefore = cutoff,
                    ContentType = contentTypeCriteria ?? new(),
                },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            placeholderIds.AddRange(page);
            if (page.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        if (placeholderIds.Count == 0)
        {
            progressReporter.Report(1, "The content metadata refresh scan completed because no item needs a refresh.");
            return;
        }

        var queued = 0;
        for (var i = 0; i < placeholderIds.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progressReporter.Report(
                GetProgress(i, placeholderIds.Count),
                $"The scan queued {queued} of {placeholderIds.Count} items.");

            var itemId = placeholderIds[i];
            try
            {
                await jobManager.ExecuteAsync(
                    new()
                    {
                        JobType = MagpieJobs.Types.ContentMetadataRefreshItem,
                        Parameters = new Dictionary<string, string>()
                        {
                            [MagpieJobs.Parameters.ContentHierarchyItemId] = itemId.ToString("D"),
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

        progressReporter.Report(
            1,
            $"The content metadata refresh scan completed and queued {queued} of {placeholderIds.Count} items.");
    }
}
