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
        Description = "Searches the indexers for releases of a content item.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Retry,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        RequesterTriggerModes = [JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTarget,
        LockPartitionFields = [MagpieJobs.Parameters.ContentHierarchyItemId],
        CoalesceTrueWinsFields = [MagpieJobs.Parameters.Force],
        Parameters =
        [
            new(
                MagpieJobs.Parameters.ContentHierarchyItemId,
                "The content hierarchy item id to search releases for.",
                DataType.String,
                true,
                null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            },
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

        await contentReleaseService.SearchAsync(
            itemId,
            context.ManualTriggerUserId,
            context.CorrelationId,
            context.ExecutionId,
            progressReporter,
            MagpieJobs.Parameters.IsForced(context.Parameters),
            cancellationToken).ConfigureAwait(false);
    }
}
