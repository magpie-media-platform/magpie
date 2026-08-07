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
    IProgressReporter? progressReporter = null) : IJob
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
        Timeout = TimeSpan.FromMinutes(2),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTypeAndTarget,
        LockPartitionFields = [MagpieJobs.Parameters.ReleaseId],
        Parameters =
        [
            new(
                MagpieJobs.Parameters.ReleaseId,
                "The release id to acquire.",
                DataType.String,
                true,
                null),
            new(
                MagpieJobs.Parameters.SuspectLastResort,
                "When true, the auto-grab decision chose this release as a flagged last-resort " +
                "Suspect grab. The resulting download row is annotated.",
                DataType.Boolean,
                false,
                null),
            new(
                MagpieJobs.Parameters.IgnoreFailedTaint,
                "When true, prior Failed download rows do not suppress this acquisition.",
                DataType.Boolean,
                false,
                null),
            new(
                MagpieJobs.Parameters.ContentHierarchyItemId,
                "The grab-target content hierarchy node.",
                DataType.String,
                true,
                null)
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

        var suspectLastResort = context.Parameters.TryGetValue(
            MagpieJobs.Parameters.SuspectLastResort,
            out var rawSuspect) && bool.TryParse(rawSuspect, out var parsedSuspect) && parsedSuspect;

        var ignoreFailedTaint = context.Parameters.TryGetValue(
            MagpieJobs.Parameters.IgnoreFailedTaint,
            out var rawIgnoreFailedTaint) &&
            bool.TryParse(rawIgnoreFailedTaint, out var parsedIgnoreFailedTaint) &&
            parsedIgnoreFailedTaint;

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
            suspectLastResort,
            ignoreFailedTaint,
            grabTargetContentHierarchyItemId,
            cancellationToken).ConfigureAwait(false);
    }
}
