using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Models.Jobs;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Downloads;

internal sealed class DownloadRefreshJob(
    IEnumerable<IDownloadClientIntegration> downloadClientIntegrations,
    IDownloadClientRepository downloadClientRepository,
    IDownloadRepository downloadRepository,
    IJobManager jobManager,
    ILogger<DownloadRefreshJob> logger,
    IDownloadService downloadService,
    TimeProvider timeProvider,
    IProgressReporter progressReporter) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.DownloadRefresh,
        DisplayName = "Download refresh",
        Description = "Polls enabled download clients and updates tracked downloads.",
        Category = "Integrations",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Retry,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters = [],
    };

    private static readonly TimeSpan FastDelay = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan CalibrationDelay = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan ModerateDelay = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan SlowDelay = TimeSpan.FromSeconds(60);

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        var userId = jobContext.ManualTriggerUserId;

        progressReporter.Report(0, "The refresh will load the download clients.");

        var enabledClients = (await downloadClientRepository.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(c => c.Enabled)
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ThenBy(c => c.Id)
            .ToArray();

        if (enabledClients.Length == 0)
        {
            logger.LogInformation("Because no download client is enabled, DownloadRefresh skipped this run");
            progressReporter.Report(1, "The refresh stopped because no enabled download client is configured.");
            return;
        }

        TimeSpan? successorDelay = null;

        for (var i = 0; i < enabledClients.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var client = enabledClients[i];
            progressReporter.ReportFraction(
                i,
                enabledClients.Length,
                $"The download refresh will process client {client.Name}.");

            var updated = new List<Guid>();
            try
            {
                var integration = downloadClientIntegrations.LastOrDefault(
                    candidate => string.Equals(candidate.ProviderType, client.ProviderType, StringComparison.Ordinal));
                if (integration is null)
                {
                    logger.LogWarning(
                        "No download client integration is available for provider type {ProviderType}",
                        client.ProviderType);
                    continue;
                }

                var clientDownloads = await downloadRepository.QueryAllAsync(
                    new DownloadQuery { DownloadClientId = client.Id },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var downloads = clientDownloads
                    .Where(d => !DownloadStatuses.Finished.Contains(d.Status) ||
                        (d.Status is DownloadStatus.Imported &&
                            d.HeldByPolicy &&
                            d.ClientRemovedAt is null))
                    .ToArray();

                if (downloads.Length == 0)
                {
                    continue;
                }

                var externalIds = (from d in downloads let id = d.ExternalId where !string.IsNullOrEmpty(id) select id)
                    .ToArray();
                BatchDownloadStatusResult batch;
                try
                {
                    batch = await integration
                        .GetDownloadsAsync(externalIds, client.Settings, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "The batch lookup for client {DownloadClientName} failed, so this refresh tick" +
                        " skipped the client",
                        client.Name);
                    continue;
                }

                foreach (var (externalId, failureEx) in batch.Failures)
                {
                    var matched = downloads.FirstOrDefault(d =>
                        string.Equals(d.ExternalId, externalId, StringComparison.Ordinal));
                    logger.LogWarning(
                        failureEx,
                        "The refresh of download {DownloadId} ({ExternalId}) failed for client {DownloadClientName}",
                        matched?.Id,
                        externalId,
                        client.Name);
                }

                foreach (var download in downloads)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!batch.Statuses.TryGetValue(download.ExternalId, out var info))
                    {
                        continue;
                    }

                    if (download.Status is DownloadStatus.Imported)
                    {
                        try
                        {
                            await downloadService
                                .ApplyImportedHoldTrackingAsync(download, info, userId, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(
                                ex,
                                "Hold tracking failed for imported download {DownloadId} on client " +
                                "{DownloadClientName}",
                                download.Id,
                                client.Name);
                        }

                        continue;
                    }

                    var isActive = info.Status is DownloadStatus.Downloading
                        or DownloadStatus.Queued
                        or DownloadStatus.Stalled;

                    var beforeStatus = download.Status;
                    var beforeProgress = download.Progress;
                    var beforeProgressAt = download.LastProgressAt;

                    try
                    {
                        if (await downloadService
                            .UpdateRefreshedClientStateAsync(download, info, userId, cancellationToken)
                            .ConfigureAwait(false))
                        {
                            updated.Add(download.Id);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(
                            ex,
                            "The job failed to apply the refreshed status of download {DownloadId} " +
                            "from client {DownloadClientName}",
                            download.Id,
                            client.Name);
                        continue;
                    }

                    if (!isActive)
                    {
                        continue;
                    }

                    var delay = ComputeSuccessorDelay(
                        beforeStatus,
                        beforeProgress,
                        beforeProgressAt,
                        info,
                        timeProvider.GetUtcNow().UtcDateTime);
                    if (delay < successorDelay || successorDelay is null)
                    {
                        successorDelay = delay;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The download refresh for client {DownloadClientName} failed", client.Name);
            }
            finally
            {
                await PublishRefreshedStatusAsync(updated, userId).ConfigureAwait(false);
            }
        }

        if (successorDelay is { } chosen)
        {
            var notBefore = timeProvider.GetUtcNow().UtcDateTime.Add(chosen);
            await jobManager.ExecuteAsync(
                new JobEnqueueRequest
                {
                    JobType = MagpieJobs.Types.DownloadRefresh,
                    IsSuccessor = true,
                    NotBeforeAt = notBefore,
                    UserId = userId,
                    Mode = JobExecutionMode.Queue,
                },
                cancellationToken).ConfigureAwait(false);

            logger.LogDebug(
                "Because downloads are active, DownloadRefresh scheduled an adaptive successor at {NotBefore:o}",
                notBefore);
        }

        progressReporter.Report(1, "The download refresh completed.");
    }

    private static TimeSpan ComputeSuccessorDelay(
        DownloadStatus beforeStatus,
        decimal? beforeProgress,
        DateTime? beforeProgressAt,
        DownloadInfo info,
        DateTime now)
    {
        if (info.Status is DownloadStatus.Queued or DownloadStatus.Stalled)
        {
            return SlowDelay;
        }

        if (info.ProgressPercent is not { } afterProgress)
        {
            return SlowDelay;
        }

        if (afterProgress >= 100m)
        {
            return FastDelay;
        }

        var wasActive = beforeStatus is DownloadStatus.Downloading
            or DownloadStatus.Queued
            or DownloadStatus.Stalled;
        if (!wasActive)
        {
            return CalibrationDelay;
        }

        if (beforeProgress is not { } startProgress || beforeProgressAt is not { } startAt)
        {
            return CalibrationDelay;
        }

        var elapsed = now - startAt;
        if (elapsed <= TimeSpan.Zero)
        {
            return CalibrationDelay;
        }

        var movement = afterProgress - startProgress;
        if (movement <= 0m)
        {
            return SlowDelay;
        }

        var etaSeconds = elapsed.TotalSeconds * (double)(100m - afterProgress) / (double)movement;

        return etaSeconds switch
        {
            <= 60d => FastDelay,
            <= 300d => CalibrationDelay,
            <= 900d => ModerateDelay,
            _ => SlowDelay,
        };
    }

    private async Task PublishRefreshedStatusAsync(List<Guid> updated, Guid? userId)
    {
        if (updated.Count == 0)
        {
            return;
        }

        try
        {
            await downloadService.PublishRefreshedStatusAsync(updated, userId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The job failed to publish the refreshed status of {DownloadCount} downloads",
                updated.Count);
        }
    }
}
