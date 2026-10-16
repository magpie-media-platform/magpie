using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Fulfillment;

internal sealed class ReleaseAutoGrabItemJob(
    IReleaseService releaseService,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IJobManager jobManager,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    TimeProvider timeProvider,
    ILogger<ReleaseAutoGrabItemJob> logger) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.ReleaseAutoGrabItem,
        DisplayName = "Release auto-grab item",
        Description = "Selects the best release for a content item and downloads it.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTypeAndTarget,
        LockPartitionFields = [MagpieJobs.Parameters.ContentItemId],
        Parameters =
        [
            new(
                MagpieJobs.Parameters.ContentItemId,
                "The content item id to run the auto-grab decision for.",
                DataType.String,
                true,
                null)
        ],
    };

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue(
            MagpieJobs.Parameters.ContentItemId,
            out var rawContentItemId) || string.IsNullOrWhiteSpace(rawContentItemId))
        {
            throw new ArgumentException($"Parameter {MagpieJobs.Parameters.ContentItemId} is required");
        }

        if (!Guid.TryParse(rawContentItemId, out var contentItemId))
        {
            throw new ArgumentException("The content item ID has an invalid format.");
        }

        var userId = context.ManualTriggerUserId;
        var correlationId = context.CorrelationId;

        if (await ResolveRunnableCandidateAsync(contentItemId, cancellationToken).ConfigureAwait(false) is not
            var (candidate, shotgun))
        {
            return;
        }

        if (!shotgun)
        {
            var best = await releaseService
                .SelectBestReleaseAsync(contentItemId, userId, correlationId, cancellationToken)
                .ConfigureAwait(false);
            if (best is not null)
            {
                await QueueReleaseAcquisitionAsync(candidate, best, userId, correlationId, cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        var releases = await releaseService
            .SelectAcceptableReleasesAsync(contentItemId, userId, correlationId, cancellationToken)
            .ConfigureAwait(false);
        foreach (var release in releases)
        {
            await QueueReleaseAcquisitionAsync(
                candidate,
                release,
                userId,
                correlationId,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(ContentItem Candidate, bool Shotgun)?> ResolveRunnableCandidateAsync(
        Guid contentItemId,
        CancellationToken cancellationToken)
    {
        var page = await contentHierarchyItemRepository
            .QueryAsync(
                new ContentHierarchyItemCriteria { ContentItemId = contentItemId },
                new PagingParameter { PageNumber = 1, PageSize = 1 },
                sort: null,
                cancellationToken)
            .ConfigureAwait(false);

        var item = page.Items.FirstOrDefault();
        if (item?.ContentItem is not { } contentItem)
        {
            logger.LogInformation(
                "Content item {ContentItemId} does not exist, so auto-grab skipped the retry",
                contentItemId);
            return null;
        }

        var candidate = contentItem with { ContentHierarchyItemId = item.Id };

        if (candidate.Fulfillment is not (FulfillmentStatus.Missing or FulfillmentStatus.Upgradable))
        {
            logger.LogInformation(
                "Content item {ContentItemId} is in fulfillment state {Fulfillment}, so auto-grab skipped the retry",
                contentItemId,
                candidate.Fulfillment);
            return null;
        }

        if (item.EffectiveMonitoring == MonitoringMode.Unmonitored)
        {
            logger.LogInformation(
                "Content item {ContentItemId} is not monitored, so auto-grab skipped the retry",
                contentItemId);
            return null;
        }

        if (item.ImportPending)
        {
            logger.LogInformation(
                "Content item {ContentItemId} has a discovered file that waits to import, so auto-grab skipped it",
                contentItemId);
            return null;
        }

        return (candidate, item.EffectiveMonitoring == MonitoringMode.Shotgun);
    }

    private async Task QueueReleaseAcquisitionAsync(
        ContentItem candidate,
        Release chosen,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            [MagpieJobs.Parameters.ReleaseId] = chosen.Id.ToString(),
            [MagpieJobs.Parameters.ContentHierarchyItemId] = candidate.ContentHierarchyItemId.ToString()
        };

        await transactionManager.ExecuteWithTransactionAsync(
            async transactionToken =>
            {
                await jobManager.ExecuteAsync(
                    new JobEnqueueRequest
                    {
                        JobType = MagpieJobs.Types.ReleaseDownloadItem,
                        Parameters = parameters,
                        Mode = JobExecutionMode.Queue,
                        UserId = userId,
                        CorrelationId = correlationId,
                    },
                    transactionToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(
                    new AuditRecord
                    {
                        Category = AuditCategories.Jobs,
                        Operation = "GrabAttempted",
                        EntityType = nameof(ContentItem),
                        EntityId = candidate.Id.ToString(),
                        Detail = $"Queued ReleaseDownloadItem for release {chosen.Id}.",
                        Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                        UserId = userId,
                        CorrelationId = correlationId
                    },
                    transactionToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Auto-grab selected release {ReleaseId} for content {ContentItemId}",
            chosen.Id,
            candidate.Id);
    }
}
