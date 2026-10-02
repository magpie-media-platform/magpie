using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

namespace Magpie.Core.Jobs.Downloads;

internal sealed class DownloadFulfilledCancellationJob(
    IDownloadRepository downloadRepository,
    IDownloadService downloadService,
    IProgressReporter progressReporter) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.DownloadFulfilledCancellation,
        DisplayName = "Cancel fulfilled downloads",
        Description = "Cancels downloads whose scopes are all fulfilled after a successful import.",
        Category = "Integrations",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Retry,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTypeAndTarget,
        LockPartitionFields = [MagpieJobs.Parameters.DownloadId],
        Parameters =
        [
            new(
                MagpieJobs.Parameters.DownloadId,
                "The successfully imported download to preserve.",
                DataType.String,
                true,
                null)
        ],
    };

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue(MagpieJobs.Parameters.DownloadId, out var rawId) ||
            !Guid.TryParse(rawId, out var importedDownloadId))
        {
            throw new ArgumentException("A valid imported download id is required.");
        }

        var candidates = await downloadRepository.ListFulfilledIdsAsync(
            importedDownloadId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var failed = 0;
        foreach (var candidate in candidates)
        {
            var outcome = await downloadService.CancelFulfilledAsync(candidate, importedDownloadId, cancellationToken)
                .ConfigureAwait(false);
            if (outcome is DownloadCancellationOutcome.ClientError or DownloadCancellationOutcome.ClientUnavailable)
            {
                failed++;
            }
        }

        if (failed > 0)
        {
            throw new InvalidOperationException($"The client could not cancel {failed} fulfilled download(s).");
        }

        progressReporter.Report(1, "Fulfilled downloads were checked.");
    }
}
