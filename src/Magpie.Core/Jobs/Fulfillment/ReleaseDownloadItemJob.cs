using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Enums;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

namespace Magpie.Core.Jobs.Fulfillment;

internal sealed class ReleaseDownloadItemJob(
    IReleaseService contentReleaseService,
    IDownloadRepository downloadRepository,
    IProgressReporter progressReporter) : IJob
{
    internal const string CapacityDeferredAt = "capacityDeferredAt";
    internal const string CapacityPreviousDownloadId = "capacityPreviousDownloadId";

    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.ReleaseDownloadItem,
        DisplayName = "Release download item",
        Description = "Downloads a selected release using an enabled download client.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTypeAndTarget,
        LockPartitionFields = [MagpieJobs.Parameters.ReleaseId],
        GuidLockPartitionFields = [MagpieJobs.Parameters.ReleaseId],
        Parameters =
        [
            new(
                CapacityPreviousDownloadId,
                "The last acquisition before this capacity wait, or an empty value for the first acquisition.",
                DataType.String,
                false,
                null),
            new(
                CapacityDeferredAt,
                "The UTC time of the capacity deferral, used to prevent a second acquisition.",
                DataType.Date,
                false,
                null),
            new(MagpieJobs.Parameters.ReleaseId, "The release id to acquire.", DataType.String, true, null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            },
            new(
                MagpieJobs.Parameters.Interactive,
                "When true, the acquisition is interactive and prior Failed or Imported downloads do not suppress " +
                    "it. The backoff after a Failed or Faulted attempt applies to it too.",
                DataType.Boolean,
                false,
                null),
            new(
                MagpieJobs.Parameters.ContentHierarchyItemId,
                "The grab-target content hierarchy node.",
                DataType.String,
                true,
                null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            }
        ],
    };

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue(MagpieJobs.Parameters.ReleaseId, out var rawReleaseId) ||
            string.IsNullOrWhiteSpace(rawReleaseId))
        {
            throw new ArgumentException("A release ID is required.");
        }

        if (!Guid.TryParse(rawReleaseId, out var releaseId))
        {
            throw new ArgumentException("The release ID has an invalid format.");
        }

        var interactive = context.Parameters.TryGetValue(MagpieJobs.Parameters.Interactive, out var rawInteractive) &&
            bool.TryParse(rawInteractive, out var parsedInteractive) &&
            parsedInteractive;

        if (!context.Parameters.TryGetValue(
            MagpieJobs.Parameters.ContentHierarchyItemId,
            out var rawGrabTarget) || string.IsNullOrWhiteSpace(rawGrabTarget))
        {
            throw new ArgumentException("A content item ID is required.");
        }

        if (!Guid.TryParse(rawGrabTarget, out var grabTargetContentHierarchyItemId))
        {
            throw new ArgumentException("The content item ID has an invalid format.");
        }

        if (context.Parameters.TryGetValue(CapacityDeferredAt, out var rawDeferredAt) &&
            DateTime.TryParse(
                rawDeferredAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var deferredAt))
        {
            var downloads = await downloadRepository.QueryAllAsync(new DownloadQuery { ReleaseId = releaseId },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            context.Parameters.TryGetValue(CapacityPreviousDownloadId, out var rawPreviousDownloadId);
            var hasPreviousDownload = Guid.TryParse(rawPreviousDownloadId, out var previousDownloadId);
            if (downloads.Any(download => (!hasPreviousDownload || download.Id != previousDownloadId) &&
                download.CreatedAt >= deferredAt))
            {
                progressReporter.Report(1, "The capacity retry stopped because an acquisition already took place.");
                return;
            }
        }

        await contentReleaseService.AcquireAsync(
            releaseId,
            context.ManualTriggerUserId,
            context.CorrelationId,
            progressReporter,
            interactive,
            grabTargetContentHierarchyItemId,
            cancellationToken).ConfigureAwait(false);
    }
}
