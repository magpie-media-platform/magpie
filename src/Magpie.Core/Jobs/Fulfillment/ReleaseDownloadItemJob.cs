using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

namespace Magpie.Core.Jobs.Fulfillment;

internal sealed class ReleaseDownloadItemJob(
    IReleaseService contentReleaseService,
    IProgressReporter progressReporter) : IJob
{
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
            new(MagpieJobs.Parameters.ReleaseId, "The release id to acquire.", DataType.String, true, null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            },
            new(
                MagpieJobs.Parameters.Interactive,
                "When true, the acquisition is interactive and prior Failed or Imported downloads do not suppress " +
                    "it.",
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
