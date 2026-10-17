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
    ILogger<DownloadRefreshJob> logger,
    IDownloadService downloadService,
    IProgressReporter progressReporter) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.DownloadRefresh,
        DisplayName = "Download refresh",
        Description = "Updates the status of downloads from the download clients.",
        Category = "Integrations",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Retry,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters = [],
    };

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

        for (var i = 0; i < enabledClients.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var client = enabledClients[i];
            progressReporter.ReportFraction(
                i,
                enabledClients.Length,
                $"The download refresh will process client {client.Name}.");

            var updated = new Dictionary<Guid, IReadOnlyList<string>>();
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

                    try
                    {
                        var changedFields = await downloadService
                            .UpdateRefreshedClientStateAsync(download, info, userId, cancellationToken)
                            .ConfigureAwait(false);
                        if (changedFields.Count > 0)
                        {
                            updated[download.Id] = changedFields;
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

        progressReporter.Report(1, "The download refresh completed.");
    }

    private async Task PublishRefreshedStatusAsync(Dictionary<Guid, IReadOnlyList<string>> updated, Guid? userId)
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
