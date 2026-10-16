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
    IQualityService qualityService,
    IReleaseEvaluationService releaseEvaluationService,
    IContentTypeRegistry contentTypeRegistry,
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    ILogger<ContentFulfillmentReconciliationJob> logger,
    IProgressReporter progressReporter) : IJob
{
    private sealed record HierarchyNode
    {
        public required Guid Id { get; set; }

        public Guid? ParentId { get; set; }

        public required string ContentType { get; set; }

        public required string Level { get; set; }

        public Guid? EffectiveQualityTierListId { get; set; }

        public ContentItem? ContentItem { get; set; }
    }

    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.ContentFulfillmentReconciliation,
        DisplayName = "Content fulfillment reconciliation",
        Description = "Updates the settings, completion status, and file quality of all content.",
        Category = "System",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        CoalesceTrueWinsFields = [MagpieJobs.Parameters.RescoreQuality],
        CoalesceWideningFields = [MagpieJobs.Parameters.QualityTierListId, MagpieJobs.Parameters.QualityTierId],
        Parameters =
        [
            new(
                MagpieJobs.Parameters.RescoreQuality,
                "Whether the reconciliation re-scores the imported files.",
                DataType.Boolean,
                false,
                "false"),
            new(
                MagpieJobs.Parameters.QualityTierListId,
                "The quality tier list to re-score. Leave this parameter empty to re-score the files of every list.",
                DataType.String,
                false,
                null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            },
            new(
                MagpieJobs.Parameters.QualityTierId,
                "The quality tier whose lists to re-score. Leave this parameter empty to re-score the files of " +
                "every list.",
                DataType.String,
                false,
                null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            }
        ],
    };

    private const int PageSize = 500;

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        var userId = jobContext.ManualTriggerUserId;

        progressReporter.Report(0, "The reconciliation will load the content leaves.");

        await contentHierarchyItemRepository.RecomputeAllEffectiveValuesAsync(cancellationToken).ConfigureAwait(false);

        var nodes = await ListHierarchyNodesAsync(cancellationToken).ConfigureAwait(false);
        var childIdsByParent = nodes
            .Where(node => node.ParentId is not null)
            .GroupBy(node => node.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(node => node.Id).ToList());
        var leaves = nodes.Where(node => !childIdsByParent.ContainsKey(node.Id)).ToList();

        if (leaves.Count == 0)
        {
            progressReporter.Report(1, "The reconciliation stopped because the content hierarchy has no leaves.");
            logger.LogInformation("Content fulfillment reconciliation found no content hierarchy leaves");
            return;
        }

        var created = await CreateMissingContentItemsAsync(leaves, userId, cancellationToken).ConfigureAwait(false);
        progressReporter.Report(0.4, "The reconciliation created the missing content items.");

        var qualityChanges = await ReconcileQualityStatusAsync(leaves, userId, cancellationToken)
            .ConfigureAwait(false);
        var parameters = jobContext.Parameters;
        if (bool.TryParse(parameters.GetValueOrDefault(MagpieJobs.Parameters.RescoreQuality), out var rescore) &&
            rescore)
        {
            IReadOnlyCollection<Guid>? tierListIds = null;
            if (Guid.TryParse(parameters.GetValueOrDefault(MagpieJobs.Parameters.QualityTierListId), out var listId))
            {
                tierListIds = [listId];
            }
            else if (Guid.TryParse(parameters.GetValueOrDefault(MagpieJobs.Parameters.QualityTierId), out var tierId))
            {
                tierListIds = (await qualityService.ListTierListsAsync(cancellationToken).ConfigureAwait(false))
                    .Where(list => list.QualityTierRanks.Any(rank => rank.QualityTierId == tierId))
                    .Select(list => list.Id)
                    .ToList();
            }

            qualityChanges += await RescoreMediaQualityAsync(nodes, tierListIds, userId, cancellationToken)
                .ConfigureAwait(false);
        }

        progressReporter.Report(0.7, "The reconciliation updated the imported file quality.");

        var rollups = await RollUpFulfillmentAsync(nodes, childIdsByParent, userId, cancellationToken)
            .ConfigureAwait(false);

        progressReporter.Report(1, "The content fulfillment reconciliation completed.");

        logger.LogInformation(
            "The reconciliation processed {LeafCount} content leaf(s), creating {CreatedCount} content " +
            "item(s), changing quality on {QualityCount}, and rolling up fulfillment on {RollupCount}",
            leaves.Count,
            created,
            qualityChanges,
            rollups);
    }

    private static FulfillmentStatus AggregateFulfillment(IReadOnlyCollection<FulfillmentStatus> childStatuses)
    {
        if (childStatuses.Count == 0)
        {
            return FulfillmentStatus.Fulfilled;
        }

        var fullyCovered = childStatuses.Count(status =>
            status is not FulfillmentStatus.Missing and not FulfillmentStatus.Partial);
        if (fullyCovered == childStatuses.Count)
        {
            return childStatuses.Contains(FulfillmentStatus.Upgradable)
                ? FulfillmentStatus.Upgradable
                : FulfillmentStatus.Fulfilled;
        }

        return childStatuses.Any(status => status != FulfillmentStatus.Missing)
            ? FulfillmentStatus.Partial
            : FulfillmentStatus.Missing;
    }

    private static IReadOnlyList<IReadOnlyList<HierarchyNode>> OrderByLevel(
        IReadOnlyList<HierarchyNode> nodes,
        IReadOnlyDictionary<Guid, HierarchyNode> nodesById,
        IReadOnlyDictionary<Guid, List<Guid>> childIdsByParent)
    {
        var levels = new List<IReadOnlyList<HierarchyNode>>();
        var level = nodes
            .Where(node => node.ParentId is null || !nodesById.ContainsKey(node.ParentId.Value))
            .ToList();
        var placed = level.Select(node => node.Id).ToHashSet();
        while (level.Count > 0)
        {
            levels.Add(level);
            level = level
                .SelectMany(node => childIdsByParent.TryGetValue(node.Id, out var childIds) ? childIds : [])
                .Where(placed.Add)
                .Select(childId => nodesById[childId])
                .ToList();
        }

        return levels;
    }

    private async Task<IReadOnlyList<HierarchyNode>> ListHierarchyNodesAsync(CancellationToken cancellationToken)
    {
        var criteria = new ContentHierarchyItemCriteria();
        var nodes = new List<HierarchyNode>();
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

            var pageItems = page.Items.ToList();
            nodes.AddRange(pageItems.Select(item => new HierarchyNode
            {
                Id = item.Id,
                ParentId = item.ParentId,
                ContentType = item.ContentType,
                Level = item.Level,
                EffectiveQualityTierListId = item.EffectiveQualityTierListId,
                ContentItem = item.ContentItem
            }));

            if (nodes.Count >= page.TotalItems || pageItems.Count == 0)
            {
                break;
            }

            pageNumber++;
        }

        return nodes;
    }

    private async Task<int> CreateMissingContentItemsAsync(
        IReadOnlyList<HierarchyNode> leaves,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var created = 0;
        foreach (var leaf in leaves.Where(leaf => leaf.ContentItem is null))
        {
            cancellationToken.ThrowIfCancellationRequested();

            leaf.ContentItem = await contentService
                .GetOrCreateContentItemAsync(
                    leaf.Id,
                    timeProvider.GetUtcNow().UtcDateTime,
                    userId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            created++;
        }

        return created;
    }

    private async Task<int> ReconcileQualityStatusAsync(
        IReadOnlyList<HierarchyNode> leaves,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var tierLists = new Dictionary<Guid, QualityTierList?>();
        var changed = 0;
        foreach (var leaf in leaves)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (leaf.ContentItem is not { } contentItem || contentItem.MediaReconciledAt is not null)
            {
                continue;
            }

            var imports = await downloadRepository.QueryAsync(
                new DownloadQuery { ContentHierarchyItemIds = [leaf.Id], Status = DownloadStatus.Imported },
                new PagingParameter { PageNumber = 1, PageSize = 1 },
                new SortParameter
                {
                    PropertyName = nameof(Download.DateCompleted),
                    Direction = SortDirection.Descending
                },
                cancellationToken).ConfigureAwait(false);
            var qualityStatus = imports.Items.FirstOrDefault() is { } download
                ? await ResolveQualityStatusAsync(download, leaf, tierLists, cancellationToken).ConfigureAwait(false)
                : null;
            if (qualityStatus == contentItem.QualityStatus)
            {
                continue;
            }

            var current = await contentRepository.GetByIdAsync(contentItem.Id, cancellationToken)
                .ConfigureAwait(false);
            if (current is null || qualityStatus == current.QualityStatus)
            {
                continue;
            }

            current.QualityStatus = qualityStatus;
            current.ModifiedAt = timeProvider.GetUtcNow().UtcDateTime;
            leaf.ContentItem = current;

            await contentRepository.UpdateAsync(current, userId, cancellationToken).ConfigureAwait(false);
            changed++;
        }

        return changed;
    }

    private async Task<int> RescoreMediaQualityAsync(
        IReadOnlyList<HierarchyNode> nodes,
        IReadOnlyCollection<Guid>? tierListIds,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var nodesById = nodes.ToDictionary(node => node.Id);
        var rootIds = new HashSet<Guid>();
        var changed = 0;
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (node.EffectiveQualityTierListId is not { } tierListId ||
                (tierListIds is not null && !tierListIds.Contains(tierListId)))
            {
                continue;
            }

            var level = contentTypeRegistry.GetDefinitionFor(node.ContentType)?.LevelDefinitions
                .FirstOrDefault(candidate => candidate.Slug == node.Level);
            if (level is null || node.ContentItem is not { MediaReconciledAt: not null } contentItem)
            {
                continue;
            }

            var files = contentItem.MediaFiles.Concat(contentItem.CoveringMediaFiles)
                .Where(file => FileSystem.IsPrimaryFile(fileSystem, level, file));
            var (quality, rank) = await releaseEvaluationService
                .ScoreMediaQualityAsync(files, tierListId, cancellationToken)
                .ConfigureAwait(false);
            if (contentItem.QualityStatus == quality && contentItem.CurrentFileQualityRank == rank)
            {
                continue;
            }

            var current = await contentRepository.GetByIdAsync(contentItem.Id, cancellationToken)
                .ConfigureAwait(false);
            if (current is null || current.QualityStatus == quality && current.CurrentFileQualityRank == rank)
            {
                continue;
            }

            await contentRepository.UpdateMediaStateAsync(
                current with
                {
                    QualityStatus = quality,
                    CurrentFileQualityRank = rank,
                    ModifiedAt = timeProvider.GetUtcNow().UtcDateTime
                },
                userId,
                cancellationToken).ConfigureAwait(false);
            changed++;

            var root = node;
            while (root.ParentId is { } parentId && nodesById.TryGetValue(parentId, out var parent))
            {
                root = parent;
            }

            rootIds.Add(root.Id);
        }

        foreach (var rootId in rootIds)
        {
            await contentRepository.ReconcileMediaStateAsync(rootId, userId, cancellationToken).ConfigureAwait(false);
        }

        return changed;
    }

    private async Task<QualityStatus?> ResolveQualityStatusAsync(
        Download download,
        HierarchyNode leaf,
        IDictionary<Guid, QualityTierList?> tierLists,
        CancellationToken cancellationToken)
    {
        var score = await releaseEvaluationService
            .ScoreDownloadForContentAsync(download, leaf.Id, cancellationToken)
            .ConfigureAwait(false);
        if (score is null)
        {
            return null;
        }

        if (score.IsAcceptable != true || score.IsCutoffMet == true)
        {
            return QualityStatus.Met;
        }

        if (leaf.EffectiveQualityTierListId is not { } tierListId)
        {
            return QualityStatus.Met;
        }

        if (!tierLists.TryGetValue(tierListId, out var tierList))
        {
            tierList = await qualityService.GetTierListAsync(tierListId, cancellationToken).ConfigureAwait(false);
            tierLists[tierListId] = tierList;
        }

        return tierList is not null && tierList.UpgradeAllowed
            ? QualityStatus.UpgradeWanted
            : QualityStatus.Met;
    }

    private async Task<int> RollUpFulfillmentAsync(
        IReadOnlyList<HierarchyNode> nodes,
        IReadOnlyDictionary<Guid, List<Guid>> childIdsByParent,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var nodesById = nodes.ToDictionary(node => node.Id);
        var levels = OrderByLevel(nodes, nodesById, childIdsByParent);
        var computed = new Dictionary<Guid, FulfillmentStatus>();
        var leafIdByNodeId = new Dictionary<Guid, Guid>();
        var stale = new List<HierarchyNode>();

        for (var level = levels.Count - 1; level >= 0; level--)
        {
            foreach (var node in levels[level])
            {
                if (!childIdsByParent.TryGetValue(node.Id, out var childIds))
                {
                    continue;
                }

                var childStatuses = childIds
                    .Select(childId => computed.TryGetValue(childId, out var childStatus)
                        ? childStatus
                        : nodesById[childId].ContentItem?.Fulfillment ?? FulfillmentStatus.Missing)
                    .ToList();
                var aggregate = AggregateFulfillment(childStatuses);
                computed[node.Id] = aggregate;
                leafIdByNodeId[node.Id] = leafIdByNodeId.TryGetValue(childIds[0], out var leafId)
                    ? leafId
                    : childIds[0];
                if (aggregate != (node.ContentItem?.Fulfillment ?? FulfillmentStatus.Missing))
                {
                    stale.Add(node);
                }
            }
        }

        var covered = new HashSet<Guid>();
        var rollups = 0;
        foreach (var node in stale)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (covered.Contains(node.Id))
            {
                continue;
            }

            await contentService
                .MaterializeFulfillmentStatusAsync(
                    leafIdByNodeId[node.Id],
                    userId,
                    includeSelf: false,
                    cancellationToken)
                .ConfigureAwait(false);
            rollups++;

            for (var ancestor = node; ancestor is not null;)
            {
                covered.Add(ancestor.Id);
                ancestor = ancestor.ParentId is { } parentId && nodesById.TryGetValue(parentId, out var parent)
                    ? parent
                    : null;
            }
        }

        return rollups;
    }
}
