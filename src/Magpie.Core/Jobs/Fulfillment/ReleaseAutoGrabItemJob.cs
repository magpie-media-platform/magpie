
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    TimeProvider timeProvider,
    ILogger<ReleaseAutoGrabItemJob> logger) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = "ReleaseAutoGrabItem",
        DisplayName = "Release auto-grab item",
        Description =
            "Chooses the best acceptable release for one content item and queues it for download. " +
            "After a failed download, the job waits before trying again to avoid repeatedly " +
            "grabbing the same release.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        Timeout = TimeSpan.FromMinutes(5),
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

        var candidate = await ResolveRunnableCandidateAsync(contentItemId, cancellationToken)
            .ConfigureAwait(false);
        if (candidate is null)
        {
            return;
        }

        var chosen = await releaseService
            .SelectBestReleaseAsync(contentItemId, userId, correlationId, cancellationToken)
            .ConfigureAwait(false);
        if (chosen is null)
        {
            return;
        }

        await QueueReleaseAcquisitionAsync(
            candidate,
            chosen,
            userId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ContentItem?> ResolveRunnableCandidateAsync(
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
        var record = item?.ContentItem is { } contentItem
            ? contentItem with { ContentHierarchyItemId = item.Id }
            : null;

        if (record is null)
        {
            logger.LogInformation(
                "Content item {ContentItemId} does not exist, so auto-grab skipped the retry",
                contentItemId);
            return null;
        }

        var candidate = record;

        if (candidate.Fulfillment is not (FulfillmentStatus.Missing or FulfillmentStatus.Upgradable))
        {
            logger.LogInformation(
                "Content item {ContentItemId} is in fulfillment state {Fulfillment}, so auto-grab skipped the retry",
                contentItemId,
                candidate.Fulfillment);
            return null;
        }

        var hierarchyItem = await contentHierarchyItemRepository
            .GetByIdAsync(candidate.ContentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (hierarchyItem?.EffectiveMonitoring != true)
        {
            logger.LogInformation(
                "Content item {ContentItemId} is not monitored ({Monitoring}), so auto-grab skipped the retry",
                contentItemId,
                hierarchyItem?.EffectiveMonitoring);
            return null;
        }

        return candidate;
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

        await jobManager.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = MagpieJobs.Types.ReleaseDownloadItem,
                Parameters = parameters,
                Mode = JobExecutionMode.Queue,
                UserId = userId,
                CorrelationId = correlationId,
            },
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Auto-grab selected release {ReleaseId} for content {ContentItemId}",
            chosen.Id,
            candidate.Id);

        await TryWriteAuditAsync(
            operation: "GrabAttempted",
            entityType: nameof(ContentItem),
            entityId: candidate.Id.ToString(),
            detail: $"Queued ReleaseDownloadItem for release {chosen.Id}.",
            userId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task TryWriteAuditAsync(
        string operation,
        string entityType,
        string entityId,
        string detail,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditRecordRepository.CreateAsync(
                new AuditRecord
                {
                    Category = AuditCategories.Jobs,
                    Operation = operation,
                    EntityType = entityType,
                    EntityId = entityId,
                    Detail = detail,
                    Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                    UserId = userId,
                    CorrelationId = correlationId
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Auto-grab failed to write the {AuditOperation} audit row for {EntityType}/{EntityId}",
                operation,
                entityType,
                entityId);
        }
    }
}
