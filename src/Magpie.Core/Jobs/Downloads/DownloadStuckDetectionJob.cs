using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Observability;
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
    IProgressReporter progressReporter) : IJob
{
    private enum CurrentClientStatusResult
    {
        NotFound,
        Refreshed,
        VerificationFailed
    }

    private readonly record struct StuckDownloadThresholds(TimeSpan Fetching, TimeSpan Stalled);

    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.DownloadStuckDetection,
        DisplayName = "Download stuck detection",
        Description = "Marks downloads as stuck if they've stopped responding, allowing Magpie to try another release.",
        Category = "System",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters =
        [
            new(
                MagpieJobs.Parameters.FetchingMinutes,
                "Minutes before a fetching download is considered stuck.",
                DataType.Number,
                false,
                "20") { ValidateScheduleValue = ValidateMinutes },
            new(
                MagpieJobs.Parameters.StalledMinutes,
                "Minutes before a stalled download is considered stuck.",
                DataType.Number,
                false,
                "90") { ValidateScheduleValue = ValidateMinutes }
        ],
    };

    private const int DefaultFetchingMinutes = 20;
    private const int DefaultStalledMinutes = 90;

    private static string? ValidateMinutes(
        string? value,
        IReadOnlyDictionary<string, string> parameters,
        DateTime utcNow) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
        minutes > utcNow.Ticks / TimeSpan.TicksPerMinute
            ? "exceeds the supported date range." : null;

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        var userId = jobContext.ManualTriggerUserId;
        var correlationId = jobContext.CorrelationId;

        progressReporter.Report(0, "The detection will get the limits for stuck downloads.");

        var thresholds = ResolveThresholds(jobContext.Parameters);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var fetchingCutoff = now - thresholds.Fetching;
        var stalledCutoff = now - thresholds.Stalled;

        progressReporter.Report(null, "The detection will load the stuck downloads.");

        var candidates = await downloadRepository
            .ListStuckCandidatesAsync(fetchingCutoff, stalledCutoff, cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            progressReporter.Report(1, "The detection found no stuck downloads.");
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
            progressReporter.ReportFraction(
                i,
                candidates.Count,
                $"The detection will change the status of {candidates.Count} stuck downloads.");

            var stalled = candidate.Status == DownloadStatus.Stalled;
            var appliedThreshold = stalled ? thresholds.Stalled : thresholds.Fetching;
            var thresholdKindTag = new KeyValuePair<string, object?>(
                "threshold_kind",
                stalled ? "stalled" : "fetching");

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
                    DownloadTelemetry.StuckDetections.Add(1, thresholdKindTag, new("outcome", "refreshed"));
                    continue;
                }

                if (currentStatusResult == CurrentClientStatusResult.VerificationFailed)
                {
                    DownloadTelemetry.StuckDetections.Add(1, thresholdKindTag, new("outcome", "verification_failed"));
                    continue;
                }

                var transitionedThisCandidate = await downloadService
                    .MarkStuckAsync(candidate, appliedThreshold, now, userId, correlationId, cancellationToken)
                    .ConfigureAwait(false);

                if (transitionedThisCandidate)
                {
                    transitioned++;
                    DownloadTelemetry.StuckDetections.Add(1, thresholdKindTag, new("outcome", "transitioned"));
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

        progressReporter.Report(1, "The stuck-download detection completed.");

        logger.LogInformation(
            "Stuck detection refreshed {Refreshed} downloads and moved {Transitioned} of {Candidates} to " +
            "Faulted/DownloadStuck (fetching threshold {FetchingThreshold}, stalled threshold {StalledThreshold})",
            refreshed,
            transitioned,
            candidates.Count,
            thresholds.Fetching,
            thresholds.Stalled);
    }

    private StuckDownloadThresholds ResolveThresholds(IReadOnlyDictionary<string, string> parameters)
    {
        var fetching = parameters.TryGetValue(MagpieJobs.Parameters.FetchingMinutes, out var rawFetching)
            ? TimeSpan.FromMinutes(ReadPositiveMinutesParameter(
                MagpieJobs.Parameters.FetchingMinutes,
                rawFetching,
                DefaultFetchingMinutes))
            : TimeSpan.FromMinutes(DefaultFetchingMinutes);
        var stalled = parameters.TryGetValue(MagpieJobs.Parameters.StalledMinutes, out var rawStalled)
            ? TimeSpan.FromMinutes(ReadPositiveMinutesParameter(
                MagpieJobs.Parameters.StalledMinutes,
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
            "Parameter {Parameter} value {Raw} is not a positive integer, so the job will use the default {Default}",
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
                "Stuck detection skipped the status check of download {DownloadId} because no integration is" +
                " registered for provider {ProviderType}",
                candidate.DownloadId,
                candidate.ProviderType);
            return CurrentClientStatusResult.NotFound;
        }

        if (candidate.Settings is null)
        {
            logger.LogWarning(
                "Client {DownloadClientName} has no settings, so stuck detection could not verify" +
                " download {DownloadId}",
                candidate.ClientName,
                candidate.DownloadId);
            return CurrentClientStatusResult.VerificationFailed;
        }

        BatchDownloadStatusResult batch;
        try
        {
            batch = await integration
                .GetDownloadsAsync([candidate.ExternalId], candidate.Settings, cancellationToken)
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
}
