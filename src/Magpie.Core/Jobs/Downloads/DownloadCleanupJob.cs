using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Downloads;

internal sealed class DownloadCleanupJob(
    IEnumerable<IDownloadClientIntegration> downloadClientIntegrations,
    IDownloadRepository downloadRepository,
    ILogger<DownloadCleanupJob> logger,
    TimeProvider timeProvider,
    IProgressReporter progressReporter) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.DownloadCleanup,
        DisplayName = "Download cleanup",
        Description =
            "Removes completed or failed downloads from the download client when cleanup settings allow it. " +
            "Download history in Magpie is kept.",
        Category = "System",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters = [],
    };

    private static string? ResolveTrigger(
        DownloadCleanupCandidate candidate,
        IReadOnlyDictionary<string, string> settings)
    {
        if (DownloadStatuses.Failures.Contains(candidate.Status))
        {
            return candidate.KeepFailedOnClient ? null : "failed";
        }

        if (!ShouldHonorClientHoldPolicy(settings))
        {
            return "hold-policy-ignored";
        }

        return candidate.HeldByPolicy ? null : "client-released";
    }

    private static bool NeedsTransfer(Download attempt, IReadOnlyDictionary<string, string> settings) =>
        !DownloadStatuses.Finished.Contains(attempt.Status) ||
        attempt is { Status: DownloadStatus.Imported, HeldByPolicy: true } && ShouldHonorClientHoldPolicy(settings);

    private static bool ShouldHonorClientHoldPolicy(IReadOnlyDictionary<string, string> settings)
    {
        if (!settings.TryGetValue(DownloadClientSettingsKeys.HonorClientHoldPolicy, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return value switch
        {
            "1" => true,
            "0" => false,
            _ => true
        };
    }

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        var userId = jobContext.ManualTriggerUserId;
        var correlationId = jobContext.CorrelationId;

        progressReporter.Report(null, "The cleanup will load matching downloads.");

        var candidates = await downloadRepository.ListCleanupCandidatesAsync(cancellationToken).ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            progressReporter.Report(1, "The cleanup stopped because no download matched.");
            logger.LogInformation("Download cleanup found no matches");
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var removed = 0;

        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[i];
            progressReporter.ReportFraction(
                i,
                candidates.Count,
                $"The cleanup will evaluate {candidates.Count} downloads.");

            if (string.IsNullOrWhiteSpace(candidate.ExternalId))
            {
                if (candidate.Status == DownloadStatus.Imported)
                {
                    if (candidate.Settings is null)
                    {
                        logger.LogWarning(
                            "Client {ClientName} has no settings, so download {DownloadId} will stay for a later" +
                            " cleanup",
                            candidate.ClientName,
                            candidate.DownloadId);
                        continue;
                    }

                    if (ResolveTrigger(candidate, candidate.Settings) is null)
                    {
                        continue;
                    }
                }

                await StampRemovedAsync(candidate, now, userId, correlationId, "no-client-entry", cancellationToken)
                    .ConfigureAwait(false);
                removed++;
                continue;
            }

            var integration = downloadClientIntegrations.LastOrDefault(
                item => string.Equals(item.ProviderType, candidate.ProviderType, StringComparison.Ordinal));
            if (integration is null)
            {
                logger.LogWarning(
                    "Download cleanup found no integration for provider {ProviderType} (client {ClientName})," +
                    " so download {DownloadId} will stay for a later cleanup",
                    candidate.ProviderType,
                    candidate.ClientName,
                    candidate.DownloadId);
                continue;
            }

            if (candidate.Settings is null)
            {
                logger.LogWarning(
                    "Client {ClientName} has no settings, so download {DownloadId} will stay for a later cleanup",
                    candidate.ClientName,
                    candidate.DownloadId);
                continue;
            }

            var settings = candidate.Settings;
            var trigger = ResolveTrigger(candidate, settings);
            if (trigger is null)
            {
                continue;
            }

            var attempts = await downloadRepository.ListTransferAttemptsAsync(candidate.DownloadId, cancellationToken)
                .ConfigureAwait(false);
            if (attempts.Any(attempt => NeedsTransfer(attempt, settings)))
            {
                continue;
            }

            bool clientConfirmed;
            try
            {
                clientConfirmed = await integration
                    .RemoveDownloadAsync(
                        candidate.ExternalId,
                        candidate.CleanupDeleteFiles,
                        settings,
                        cancellationToken)
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
                    "The removal of download {DownloadId} ({ExternalId}) from client {ClientName} failed, so" +
                    " the download will stay unstamped for the next cleanup",
                    candidate.DownloadId,
                    candidate.ExternalId,
                    candidate.ClientName);
                continue;
            }

            if (!clientConfirmed)
            {
                logger.LogWarning(
                    "Download client {ClientName} did not confirm removal of download {DownloadId} ({ExternalId})," +
                    " so the download will stay unstamped for the next cleanup",
                    candidate.ClientName,
                    candidate.DownloadId,
                    candidate.ExternalId);
                continue;
            }

            await StampRemovedAsync(candidate, now, userId, correlationId, trigger, cancellationToken)
                .ConfigureAwait(false);
            foreach (var attempt in attempts.Where(attempt => attempt.Id != candidate.DownloadId))
            {
                await StampRemovedAsync(
                    candidate with { DownloadId = attempt.Id, Title = attempt.Title },
                    now,
                    userId,
                    correlationId,
                    trigger,
                    cancellationToken)
                    .ConfigureAwait(false);
            }

            removed++;
        }

        progressReporter.Report(1, "The download cleanup completed.");

        logger.LogInformation(
            "Download cleanup removed {Removed} of {Candidates} client entries",
            removed,
            candidates.Count);
    }

    private async Task StampRemovedAsync(
        DownloadCleanupCandidate candidate,
        DateTime now,
        Guid? userId,
        Guid? correlationId,
        string trigger,
        CancellationToken cancellationToken)
    {
        try
        {
            await downloadRepository.StampClientRemovedAsync(
                candidate.DownloadId,
                candidate.Title,
                candidate.ClientName,
                trigger,
                now,
                userId,
                correlationId,
                cancellationToken)
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
                "The download client removed download {DownloadId}, but the ClientRemovedAt stamp failed, so" +
                " the next cleanup will retry the stamp",
                candidate.DownloadId);
        }
    }
}
