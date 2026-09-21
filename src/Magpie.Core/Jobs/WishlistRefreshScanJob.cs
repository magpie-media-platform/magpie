using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class WishlistRefreshScanJob(
    IWishlistRepository wishlistRepository,
    IJobManager jobManager,
    IProgressReporter progressReporter,
    ILogger<WishlistRefreshScanJob> logger) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.WishlistRefreshScan,
        DisplayName = "Wishlist refresh scan",
        Description = "Queues a refresh for every monitored wishlist.",
        Category = "Content",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Requeue,
        Timeout = TimeSpan.FromMinutes(30),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
    };

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        progressReporter.Report(0, "The scan will load monitored wishlists.");

        var wishlists = await wishlistRepository.QueryAllAsync(
            new WishlistQuery { Monitored = true },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (wishlists.Count == 0)
        {
            progressReporter.Report(1, "The scan stopped because no monitored wishlist needs a refresh.");
            return;
        }

        var completed = 0;
        foreach (var wishlist in wishlists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progressReporter.ReportFraction(completed, wishlists.Count);

            try
            {
                await EnqueueRefreshAsync(
                    wishlist.Id,
                    context.ManualTriggerUserId,
                    context.CorrelationId,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The wishlist refresh scan failed to queue a refresh for wishlist {WishlistId} ({WishlistName})",
                    wishlist.Id,
                    wishlist.Name);
            }

            completed++;
        }

        progressReporter.Report(1, "The wishlist refresh scan completed.");
    }

    private async Task EnqueueRefreshAsync(
        Guid wishlistId,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        await jobManager.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = MagpieJobs.Types.WishlistRefreshItem,
                Parameters = new Dictionary<string, string>
                {
                    [MagpieJobs.Parameters.WishlistId] = wishlistId.ToString("D"),
                },
                Mode = JobExecutionMode.Queue,
                UserId = userId,
                CorrelationId = correlationId,
            },
            cancellationToken).ConfigureAwait(false);

        logger.LogDebug("The job queued a wishlist refresh for wishlist {WishlistId}", wishlistId);
    }
}
