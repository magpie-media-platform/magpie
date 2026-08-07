using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
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
    IDataProtectionService protector,
    ILogger<DownloadCleanupJob> logger,
    TimeProvider timeProvider,
    IProgressReporter? progressReporter = null) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = "DownloadCleanup",
        DisplayName = "Download cleanup",
        Description =
            "Removes completed or failed downloads from the download client when cleanup settings allow it. " +
            "Download history in Magpie is kept.",
        Category = "System",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        Timeout = TimeSpan.FromMinutes(10),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters = [],
    };

    private static string? ResolveTrigger(
        DownloadCleanupCandidate candidate,
        IReadOnlyDictionary<string, string> settings)
    {
        if (candidate.Status == DownloadStatus.Failed)
        {
            return candidate.KeepFailedOnClient ? null : "failed";
        }

        if (!ShouldHonorClientHoldPolicy(settings))
        {
            return "hold-policy-ignored";
        }

        return candidate.HeldByPolicy ? null : "client-released";
    }

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
        var progress = progressReporter;

        progress?.Report(null, "The cleanup will load matching downloads.");

        var candidates = await downloadRepository.ListCleanupCandidatesAsync(cancellationToken).ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            progress?.Report(1, "The cleanup stopped because no download matched.");
            logger.LogInformation("Download cleanup found no matches");
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var removed = 0;

        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[i];
            progress?.ReportFraction(
                i,
                candidates.Count,
                $"The cleanup will evaluate {candidates.Count} downloads.");

            if (string.IsNullOrWhiteSpace(candidate.ExternalId))
            {
                if (candidate.Status == DownloadStatus.Imported)
                {
                    if (!TryReadSettings(candidate, out var noClientEntrySettings) ||
                        ResolveTrigger(candidate, noClientEntrySettings) is null)
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
                    "Download cleanup found no integration for provider {ProviderType} (client {ClientName})." +
                    " Download {DownloadId} will stay for a later cleanup",
                    candidate.ProviderType,
                    candidate.ClientName,
                    candidate.DownloadId);
                continue;
            }

            if (!TryReadSettings(candidate, out var settings))
            {
                continue;
            }

            var trigger = ResolveTrigger(candidate, settings);
            if (trigger is null)
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
                    "The removal of download {DownloadId} ({ExternalId}) from client {ClientName} failed." +
                    " The download will stay unstamped for the next cleanup",
                    candidate.DownloadId,
                    candidate.ExternalId,
                    candidate.ClientName);
                continue;
            }

            if (!clientConfirmed)
            {
                logger.LogWarning(
                    "Download client {ClientName} did not confirm removal of download {DownloadId} ({ExternalId})." +
                    " The download will stay unstamped for the next cleanup",
                    candidate.ClientName,
                    candidate.DownloadId,
                    candidate.ExternalId);
                continue;
            }

            await StampRemovedAsync(candidate, now, userId, correlationId, trigger, cancellationToken)
                .ConfigureAwait(false);
            removed++;
        }

        progress?.Report(1, "The download cleanup completed.");

        logger.LogInformation(
            "Download cleanup removed {Removed} of {Candidates} client entries",
            removed,
            candidates.Count);
    }

    private bool TryReadSettings(DownloadCleanupCandidate candidate, out Dictionary<string, string> settings)
    {
        try
        {
            settings = protector.Unprotect(candidate.Settings);
            return true;
        }
        catch (Exception ex)
        {
            settings = [];
            logger.LogWarning(
                ex,
                "Download cleanup failed to read the settings for client {ClientName}," +
                " so download {DownloadId} will stay for a later cleanup",
                candidate.ClientName,
                candidate.DownloadId);
            return false;
        }
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
                "The download client removed download {DownloadId}, but the ClientRemovedAt stamp failed." +
                " The next cleanup will retry the stamp",
                candidate.DownloadId);
        }
    }
}
