using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Downloads;

internal sealed class DownloadStuckDetectionJob(
    IEnumerable<IDownloadClientIntegration> downloadClientIntegrations,
    IDownloadRepository downloadRepository,
    ILogger<DownloadStuckDetectionJob> logger,
    TimeProvider timeProvider,
    IDownloadService downloadService,
    IDataProtectionService protector,
    IProgressReporter? progressReporter = null) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = "DownloadStuckDetection",
        DisplayName = "Download stuck detection",
        Description = "Marks downloads as stuck if they've stopped responding, allowing Magpie to try another release.",
        Category = "System",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        Timeout = TimeSpan.FromMinutes(10),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters =
        [
            new(
                FetchingMinutesParameter,
                "Minutes before a fetching download is considered stuck.",
                DataType.Number,
                false,
                "20"),
            new(
                StalledMinutesParameter,
                "Minutes before a stalled download is considered stuck.",
                DataType.Number,
                false,
                "90")
        ],
    };

    private const int DefaultFetchingMinutes = 20;
    private const int DefaultStalledMinutes = 90;
    private const string FetchingMinutesParameter = "fetchingMinutes";
    private const string StalledMinutesParameter = "stalledMinutes";

    private enum CurrentClientStatusResult
    {
        NotFound,
        Refreshed,
        VerificationFailed
    }

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        var userId = jobContext.ManualTriggerUserId;
        var correlationId = jobContext.CorrelationId;
        var progress = progressReporter;

        progress?.Report(0, "The detection will get the limits for stuck downloads.");

        var thresholds = ResolveThresholds(jobContext.Parameters);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var fetchingCutoff = now - thresholds.Fetching;
        var stalledCutoff = now - thresholds.Stalled;

        progress?.Report(null, "The detection will load the stuck downloads.");

        var candidates = await downloadRepository
            .ListStuckCandidatesAsync(fetchingCutoff, stalledCutoff, cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            progress?.Report(1, "The detection found no stuck downloads.");
            logger.LogInformation(
                "Stuck detection found no stuck downloads (fetching threshold {FetchingThreshold}, " +
                "stalled threshold {StalledThreshold})",
                thresholds.Fetching,
                thresholds.Stalled);
            return;
        }

        var refreshed = 0;
        var transitioned = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[i];
            progress?.ReportFraction(
                i,
                candidates.Count,
                $"The detection will change the status of {candidates.Count} stuck downloads.");

            var appliedThreshold = candidate.Status == DownloadStatus.Stalled
                ? thresholds.Stalled
                : thresholds.Fetching;

            try
            {
                var currentStatusResult = await TryApplyCurrentClientStatusAsync(
                    candidate,
                    userId,
                    now,
                    stalledCutoff,
                    cancellationToken).ConfigureAwait(false);
                if (currentStatusResult == CurrentClientStatusResult.Refreshed)
                {
                    refreshed++;
                    continue;
                }

                if (currentStatusResult == CurrentClientStatusResult.VerificationFailed)
                {
                    continue;
                }

                var transitionedThisCandidate = await downloadService
                    .MarkStuckAsync(candidate, appliedThreshold, now, userId, correlationId, cancellationToken)
                    .ConfigureAwait(false);

                if (transitionedThisCandidate)
                {
                    transitioned++;
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
                    "Stuck detection failed to transition download {DownloadId}",
                    candidate.DownloadId);
            }
        }

        progress?.Report(1, "The stuck-download detection completed.");

        logger.LogInformation(
            "Stuck detection refreshed {Refreshed} downloads and moved {Transitioned} of {Candidates} to " +
            "Failed/DownloadStuck (fetching threshold {FetchingThreshold}, stalled threshold {StalledThreshold})",
            refreshed,
            transitioned,
            candidates.Count,
            thresholds.Fetching,
            thresholds.Stalled);
    }

    private StuckDownloadThresholds ResolveThresholds(IReadOnlyDictionary<string, string> parameters)
    {
        var fetching = parameters.TryGetValue(FetchingMinutesParameter, out var rawFetching)
            ? TimeSpan.FromMinutes(ReadPositiveMinutesParameter(
                FetchingMinutesParameter,
                rawFetching,
                DefaultFetchingMinutes))
            : TimeSpan.FromMinutes(DefaultFetchingMinutes);
        var stalled = parameters.TryGetValue(StalledMinutesParameter, out var rawStalled)
            ? TimeSpan.FromMinutes(ReadPositiveMinutesParameter(
                StalledMinutesParameter,
                rawStalled,
                DefaultStalledMinutes))
            : TimeSpan.FromMinutes(DefaultStalledMinutes);

        return new StuckDownloadThresholds(fetching, stalled);
    }

    private int ReadPositiveMinutesParameter(string parameterName, string raw, int defaultMinutes)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured) && configured > 0)
        {
            return configured;
        }

        logger.LogWarning(
            "Parameter {Parameter} value {Raw} is not a positive integer. The job will use the default {Default}",
            parameterName,
            raw,
            defaultMinutes);

        return defaultMinutes;
    }

    private async Task<CurrentClientStatusResult> TryApplyCurrentClientStatusAsync(
        StuckDownloadCandidate candidate,
        Guid? userId,
        DateTime now,
        DateTime stalledCutoff,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(candidate.ExternalId))
        {
            return CurrentClientStatusResult.NotFound;
        }

        var integration = downloadClientIntegrations.LastOrDefault(
            item => string.Equals(item.ProviderType, candidate.ProviderType, StringComparison.Ordinal));
        if (integration is null)
        {
            logger.LogWarning(
                "Stuck detection skipped the status check of download {DownloadId}." +
                " No integration is registered for provider {ProviderType}",
                candidate.DownloadId,
                candidate.ProviderType);
            return CurrentClientStatusResult.NotFound;
        }

        BatchDownloadStatusResult batch;
        try
        {
            batch = await integration
                .GetDownloadsAsync([candidate.ExternalId], protector.Unprotect(candidate.Settings), cancellationToken)
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
                "Stuck detection failed to get the status of download {DownloadId} from client {DownloadClientName}",
                candidate.DownloadId,
                candidate.ClientName);
            return CurrentClientStatusResult.VerificationFailed;
        }

        if (batch.Failures.TryGetValue(candidate.ExternalId, out var failure))
        {
            logger.LogWarning(
                failure,
                "The status request for download {DownloadId} ({ExternalId}) failed on client {DownloadClientName}",
                candidate.DownloadId,
                candidate.ExternalId,
                candidate.ClientName);
            return CurrentClientStatusResult.VerificationFailed;
        }

        if (!batch.Statuses.TryGetValue(candidate.ExternalId, out var info))
        {
            return CurrentClientStatusResult.NotFound;
        }

        var applied = await downloadService
            .TryApplyVerifiedClientStateAsync(candidate, info, stalledCutoff, now, userId, cancellationToken)
            .ConfigureAwait(false);

        return applied ? CurrentClientStatusResult.Refreshed : CurrentClientStatusResult.NotFound;
    }

    private readonly record struct StuckDownloadThresholds(TimeSpan Fetching, TimeSpan Stalled);
}
