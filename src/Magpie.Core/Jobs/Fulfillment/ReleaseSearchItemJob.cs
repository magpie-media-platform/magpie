using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

namespace Magpie.Core.Jobs.Fulfillment;

internal sealed class ReleaseSearchItemJob(
    IReleaseSearchService contentReleaseService,
    IProgressReporter progressReporter) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.ReleaseSearchItem,
        DisplayName = "Release search item",
        Description = "Searches available releases for one content item.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Retry,
        Timeout = TimeSpan.FromMinutes(10),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        RequesterTriggerModes = [JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTarget,
        LockPartitionFields = [MagpieJobs.Parameters.ContentHierarchyItemId],
        Parameters =
        [
            new(
                MagpieJobs.Parameters.ContentHierarchyItemId,
                "The content hierarchy item id to search releases for.",
                DataType.String,
                true,
                null),
            new(
                MagpieJobs.Parameters.Force,
                "Whether the search ignores the recent-search coalescing window.",
                DataType.Boolean,
                false,
                "false")
        ],
    };

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue(
            MagpieJobs.Parameters.ContentHierarchyItemId,
            out var rawItemId) || string.IsNullOrWhiteSpace(rawItemId))
        {
            throw new ArgumentException("A content item ID is required.");
        }

        if (!Guid.TryParse(rawItemId, out var itemId))
        {
            throw new ArgumentException("The content item ID has an invalid format.");
        }

        var forced = context.ExecutionMode is JobExecutionMode.Immediate ||
            (context.Parameters.TryGetValue(MagpieJobs.Parameters.Force, out var rawForce) &&
                bool.TryParse(rawForce, out var force) &&
                force);

        await contentReleaseService.SearchAsync(
            itemId,
            context.ManualTriggerUserId,
            context.CorrelationId,
            context.ExecutionId,
            progressReporter,
            forced,
            cancellationToken).ConfigureAwait(false);
    }
}
