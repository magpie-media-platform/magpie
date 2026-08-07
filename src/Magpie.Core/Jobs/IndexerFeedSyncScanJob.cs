using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class IndexerFeedSyncScanJob(
    IIndexerFeedRepository indexerFeedRepository,
    IJobManager jobManager,
    ILogger<IndexerFeedSyncScanJob> logger,
    IProgressReporter? progressReporter = null) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = "IndexerFeedSyncScan",
        DisplayName = "Indexer feed sync scan",
        Description = "Queues a sync for every enabled indexer feed.",
        Category = "Integrations",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Retry,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
    };

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        var userId = jobContext.ManualTriggerUserId;
        var correlationId = jobContext.CorrelationId;
        var progress = progressReporter;

        progress?.Report(0, "The scan will load the indexer feeds.");

        var feeds = (await indexerFeedRepository.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(f => f.Enabled)
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ThenBy(f => f.Id)
            .ToArray();

        for (var i = 0; i < feeds.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var feed = feeds[i];
            progress?.ReportFraction(i, feeds.Length, $"The scan will queue a sync for indexer feed {feed.Name}.");

            try
            {
                await EnqueueSyncAsync(feed.Id, userId, correlationId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The indexer feed sync scan failed to queue the sync for feed {FeedId}", feed.Id);
            }
        }

        progress?.Report(1, "The indexer-feed sync scan completed.");
    }

    private async Task EnqueueSyncAsync(
        Guid feedId,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        await jobManager.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = IndexerFeedSyncItemJob.Spec.JobType,
                Parameters = new Dictionary<string, string>
                {
                    [MagpieJobs.Parameters.FeedId] = feedId.ToString()
                },
                Mode = JobExecutionMode.Queue,
                UserId = userId,
                CorrelationId = correlationId,
            },
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation("The job queued the indexer feed sync for feed {FeedId}", feedId);
    }
}
