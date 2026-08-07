using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Fulfillment;

internal sealed class ContentFulfillmentReconciliationJob(
    IContentService contentService,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IContentRepository contentRepository,
    IDownloadRepository downloadRepository,
    IReleaseRepository releaseRepository,
    IQualityService qualityService,
    IReleaseEvaluationService releaseEvaluationService,
    TimeProvider timeProvider,
    ILogger<ContentFulfillmentReconciliationJob> logger,
    IProgressReporter? progressReporter = null) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = "ContentFulfillmentReconciliation",
        DisplayName = "Content fulfillment reconciliation",
        Description = "Recalculates effective settings, completion status, and imported file quality.",
        Category = "System",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        Timeout = TimeSpan.FromMinutes(30),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters = [],
    };

    private const int PageSize = 500;

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        var userId = jobContext.ManualTriggerUserId;
        var correlationId = jobContext.CorrelationId;
        var progress = progressReporter;

        progress?.Report(0, "The reconciliation will load the content leaves.");

        await contentHierarchyItemRepository.RecomputeAllEffectiveValuesAsync(cancellationToken).ConfigureAwait(false);

        var leafIds = await ListLeafHierarchyItemIdsAsync(cancellationToken).ConfigureAwait(false);

        if (leafIds.Count == 0)
        {
            progress?.Report(1, "The reconciliation stopped because the content hierarchy has no leaves.");
            logger.LogInformation("Content fulfillment reconciliation found no content hierarchy leaves");
            return;
        }

        var processed = 0;
        foreach (var leafId in leafIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await contentService
                .GetOrCreateContentItemAsync(
                    leafId,
                    timeProvider.GetUtcNow().UtcDateTime,
                    userId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await RecomputeQualityStatusAsync(leafId, userId, cancellationToken).ConfigureAwait(false);

            await contentService
                .MaterializeFulfillmentStatusAsync(leafId, userId, includeSelf: false, cancellationToken)
                .ConfigureAwait(false);

            processed++;
            progress?.Report((double)processed / leafIds.Count, "The reconciliation updated fulfillment and quality.");
        }

        progress?.Report(1, "The content fulfillment reconciliation completed.");

        logger.LogInformation("Reconciled {LeafCount} content leaves", leafIds.Count);
    }

    private async Task RecomputeQualityStatusAsync(Guid leafId, Guid? userId, CancellationToken cancellationToken)
    {
        var contentItems = await contentRepository.QueryAsync(
            new() { ContentHierarchyItemId = leafId },
            new() { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false);

        var contentItem = contentItems.Items.FirstOrDefault();
        if (contentItem is null)
        {
            return;
        }

        var downloads = await downloadRepository.QueryAsync(
            new DownloadQuery
            {
                ContentItemId = leafId,
                Status = DownloadStatus.Imported
            },
            new PagingParameter { PageNumber = 1, PageSize = 1 },
            new SortParameter
            {
                PropertyName = nameof(Download.DateCompleted),
                Direction = SortDirection.Descending
            },
            cancellationToken).ConfigureAwait(false);

        var qualityStatus = await ResolveQualityStatusAsync(
            downloads.Items.FirstOrDefault(),
            leafId,
            cancellationToken).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        contentItem.QualityStatus = qualityStatus;
        contentItem.ModifiedAt = now;

        await contentRepository.UpdateAsync(contentItem, userId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<QualityStatus?> ResolveQualityStatusAsync(
        Download? download,
        Guid leafId,
        CancellationToken cancellationToken)
    {
        if (download?.ReleaseId is not { } releaseId)
        {
            return null;
        }

        var release = await releaseRepository.GetByIdAsync(releaseId, cancellationToken).ConfigureAwait(false);
        if (release is null)
        {
            return null;
        }

        var score = await releaseEvaluationService
            .ScoreForContentAsync(release, leafId, currentQualityOrdinal: null, cancellationToken)
            .ConfigureAwait(false);
        if (score is null)
        {
            return null;
        }

        if (score.IsAcceptable != true || score.IsCutoffMet == true)
        {
            return QualityStatus.Met;
        }

        var node = await contentHierarchyItemRepository
            .GetByIdAsync(leafId, cancellationToken)
            .ConfigureAwait(false);
        var tierList = node?.EffectiveQualityTierListId is { } tierListId
            ? await qualityService.GetTierListAsync(tierListId, cancellationToken).ConfigureAwait(false)
            : null;
        return tierList is not null && tierList.UpgradeAllowed
            ? QualityStatus.UpgradeWanted
            : QualityStatus.Met;
    }

    private async Task<IReadOnlyList<Guid>> ListLeafHierarchyItemIdsAsync(CancellationToken cancellationToken)
    {
        var criteria = new ContentHierarchyItemCriteria { LeafOnly = true };
        var ids = new List<Guid>();
        var pageNumber = 1;
        while (true)
        {
            var page = await contentHierarchyItemRepository
                .QueryAsync(
                    criteria,
                    new PagingParameter { PageNumber = pageNumber, PageSize = PageSize },
                    sort: null,
                    cancellationToken)
                .ConfigureAwait(false);

            ids.AddRange(page.Items.Select(item => item.Id));
            if (ids.Count >= page.TotalItems || !page.Items.Any())
            {
                break;
            }

            pageNumber++;
        }

        return ids;
    }
}
