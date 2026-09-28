using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Paging;
using Magpie.Models.Patch;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;

using ContentEventImpact = Magpie.Sdk.Events.ContentEventImpact;

namespace Magpie.Core.Repositories;

internal sealed class ContentHierarchyItemRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IMetadataContainerRepository metadataContainerRepository,
    IContentTypeRegistry contentTypeRegistry,
    IEventBus eventBus,
    ILogger<ContentHierarchyItemRepository> logger,
    TimeProvider timeProvider,
    IContentRepository contentRepository) : IContentHierarchyItemRepository
{
    private static readonly LibraryImportCandidateStatus[] ImportPendingStatuses =
    [
        LibraryImportCandidateStatus.Pending,
        LibraryImportCandidateStatus.MatchingPending,
        LibraryImportCandidateStatus.Queued,
        LibraryImportCandidateStatus.Importing
    ];

    private const string ExternalIdKeySeparator = "\u001f";

    private const string CreateSavepoint = "content_hierarchy_item_create";

    private static readonly StringComparer MediaFilePathComparer = StringComparer.Create(
        CultureInfo.InvariantCulture,
        CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    private const int AncestryBatchSize = 1_000;

    private static readonly string[] MetadataSortProperties =
    [
        nameof(MaterializedMetadata.PublishDate),
        nameof(MaterializedMetadata.Duration),
        nameof(MaterializedMetadata.SequenceNumber),
        nameof(MaterializedMetadata.SortTitle),
    ];

    private readonly TimeProvider _time = timeProvider;

    private enum EffectiveValueTriggerKind
    {
        Full,
        ItemSubtree,
        Reparent,
        RootFolder,
        QualityTierList,
        GlobalMonitoring
    }

    private static void ApplyPatch(
        IDictionary<string, string> values,
        DictionaryPatch<string, string, string> patch)
    {
        if (patch.Add is not null)
        {
            foreach (var (key, value) in patch.Add)
            {
                values[key] = value ?? string.Empty;
            }
        }

        if (patch.Update is not null)
        {
            foreach (var (key, value) in patch.Update)
            {
                if (values.ContainsKey(key))
                {
                    values[key] = value ?? string.Empty;
                }
            }
        }

        if (patch.Remove is not null)
        {
            foreach (var key in patch.Remove)
            {
                values.Remove(key);
            }
        }
    }

    private static IQueryable<EffectiveNode> ToEffectiveNodes(IQueryable<ContentHierarchyItem> items)
    {
        return
            from item in items
            select new EffectiveNode(
                item.Id,
                item.ParentId,
                item.ContentType,
                item.Level,
                item.MetadataContainerId,
                item.Monitoring,
                item.RootFolderId,
                item.QualityTierListId,
                item.EffectiveMonitoring,
                item.EffectiveRootFolderId,
                item.EffectiveQualityTierListId);
    }

    private static IQueryable<ContentHierarchyItem> IncludeReadModel(IQueryable<ContentHierarchyItem> query)
    {
        var withIncludes = query
            .Include(i => i.ContentItem)
            .ThenInclude(i => i!.MediaFiles)
            .Include(i => i.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.AlternateTexts)
            .Include(i => i.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.Awards)
            .Include(i => i.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.ContentRatings)
            .Include(i => i.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.CustomMetadata)
            .Include(i => i.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.ExternalIds)
            .Include(i => i.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.Genres)
            .Include(i => i.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.Images)
            .Include(i => i.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.Previews)
            .Include(i => i.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.References)
            .Include(i => i.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.Relationships);
        return withIncludes.AsSplitQuery();
    }

    private IQueryable<Guid> ImportPendingItemIds()
    {
        var targetIds =
            from candidate in context.LibraryImportCandidates.AsNoTracking()
            where candidate.ContentHierarchyItemId != null && ImportPendingStatuses.Contains(candidate.Status)
            select candidate.ContentHierarchyItemId!.Value;
        return
            from item in context.ContentHierarchyItems.AsNoTracking()
            where targetIds.Contains(item.Id) && (item.ContentItem == null ||
                (item.ContentItem.MediaReconciledAt != null
                    ? item.ContentItem.PrimaryMediaFileCount == 0
                    : !item.ContentItem.MediaFiles.Any()))
            select item.Id;
    }

    private async Task MarkImportPendingAsync(
        IReadOnlyCollection<ContentHierarchyItem> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var ids = items.Select(item => item.Id).ToList();
        var pending = (await ImportPendingItemIds()
            .Where(id => EF.Parameter(ids).Contains(id))
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).ToHashSet();
        foreach (var item in items)
        {
            item.ImportPending = pending.Contains(item.Id);
        }
    }

    private async Task LoadTagNamesAsync(
        IReadOnlyCollection<ContentHierarchyItem> items,
        CancellationToken cancellationToken)
    {
        var containerIds = items.Select(item => item.MetadataContainerId).Distinct().ToList();
        var tagged = await (
                from tag in context.Tags.AsNoTracking()
                from container in tag.MetadataContainers
                where EF.Parameter(containerIds).Contains(container.Id)
                select new { ContainerId = container.Id, tag.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var namesByContainer = tagged.ToLookup(
            row => row.ContainerId,
            row => row.Name);
        foreach (var item in items)
        {
            item.Tags = [.. namesByContainer[item.MetadataContainerId].Order(StringComparer.Ordinal)];
        }
    }

    public async Task<IEnumerable<ContentHierarchyItem>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var items = await IncludeReadModel(context.ContentHierarchyItems.AsNoTracking())
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            OrderMediaFiles(items);
            await MarkImportPendingAsync(items, cancellationToken).ConfigureAwait(false);
            return items;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The hierarchy item repository failed to list the items.");
            throw;
        }
    }

    public async Task<ContentHierarchyItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var item = await IncludeReadModel(context.ContentHierarchyItems.AsNoTracking())
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
                .ConfigureAwait(false);
            OrderMediaFiles(item is null ? [] : [item]);
            await MarkImportPendingAsync(item is null ? [] : [item], cancellationToken).ConfigureAwait(false);
            return item;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The hierarchy item repository failed to read item {ContentHierarchyItemId}.", id);
            throw;
        }
    }

    public async Task<IReadOnlyList<ContentHierarchyItem>> GetLineageAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var chainIds = new List<Guid>();
            var visited = new HashSet<Guid>();
            var currentId = (Guid?)contentHierarchyItemId;

            while (currentId is { } id && visited.Add(id))
            {
                var row = await context.ContentHierarchyItems
                    .AsNoTracking()
                    .Where(i => i.Id == id)
                    .Select(i => new { i.ParentId })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (row is null)
                {
                    break;
                }

                chainIds.Add(id);
                currentId = row.ParentId;
            }

            var nodesById = await IncludeReadModel(context.ContentHierarchyItems.AsNoTracking())
                .Where(i => chainIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, cancellationToken)
                .ConfigureAwait(false);
            var chain = chainIds
                .Select(id => nodesById.GetValueOrDefault(id))
                .OfType<ContentHierarchyItem>()
                .ToList();
            OrderMediaFiles(chain);
            await MarkImportPendingAsync(chain, cancellationToken).ConfigureAwait(false);
            return chain;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The hierarchy item repository failed to read the lineage of item {ContentHierarchyItemId}.",
                contentHierarchyItemId);
            throw;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, ContentHierarchyItemAncestry>> GetAncestryAsync(
        IReadOnlyCollection<Guid> contentHierarchyItemIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>();
            var level = contentHierarchyItemIds.Distinct().ToList();
            while (level.Count > 0)
            {
                var rows = new List<ContentHierarchyItemAncestry>();
                foreach (var batch in level.Chunk(AncestryBatchSize))
                {
                    rows.AddRange(await context.ContentHierarchyItems
                        .AsNoTracking()
                        .Where(item => batch.Contains(item.Id))
                        .Select(item => new ContentHierarchyItemAncestry
                        {
                            Id = item.Id,
                            ParentId = item.ParentId,
                            EffectiveMonitoring = item.EffectiveMonitoring
                        })
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false));
                }

                foreach (var row in rows)
                {
                    ancestry[row.Id] = row;
                }

                level = [.. rows
                    .Where(row => row.ParentId is { } parentId && !ancestry.ContainsKey(parentId))
                    .Select(row => row.ParentId!.Value)
                    .Distinct()];
            }

            return ancestry;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The hierarchy item repository failed to read the ancestry of the items.");
            throw;
        }
    }

    public async Task LockAsync(Guid contentHierarchyItemId, CancellationToken cancellationToken = default)
    {
        try
        {
            await context.ContentHierarchyItems
                .Where(item => item.Id == contentHierarchyItemId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.ModifiedAt, item => item.ModifiedAt),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The hierarchy item repository failed to lock item {ContentHierarchyItemId}.",
                contentHierarchyItemId);
            throw;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetChildCountsByParentAsync(
        IReadOnlyCollection<Guid> parentIds,
        CancellationToken cancellationToken = default)
    {
        if (parentIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        try
        {
            var rows = await (
                from i in context.ContentHierarchyItems.AsNoTracking()
                where i.ParentId != null && parentIds.Contains(i.ParentId.Value)
                group i by i.ParentId!.Value into g
                select new { ParentId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return rows.ToDictionary(r => r.ParentId, r => r.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The hierarchy item repository failed to read child counts");
            throw;
        }
    }

    public async Task<IReadOnlyList<DuplicateRoots>> FindDuplicateRootsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rootIdentities =
                from item in context.ContentHierarchyItems.AsNoTracking()
                where item.ParentId == null
                join contribution in context.MetadataContributions.AsNoTracking()
                    on item.MetadataContainerId equals contribution.MetadataContainerId
                join externalId in DeduplicatedExternalIds()
                    on contribution.Id equals externalId.MetadataContributionId
                where externalId.Source != null && externalId.Source != "" && externalId.Value != ""
                select new
                {
                    item.ContentType,
                    item.Level,
                    Source = externalId.Source!.ToLower(),
                    Value = externalId.Value.ToLower(),
                    ItemId = item.Id,
                    ItemName = item.Name
                };

            var sharedKeys = await rootIdentities
                .Distinct()
                .GroupBy(row => new { row.ContentType, row.Level, row.Source, row.Value })
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (sharedKeys.Count == 0)
            {
                return [];
            }

            var sharedValues = sharedKeys.Select(key => key.Value).Distinct().ToList();
            var rows = await rootIdentities
                .Where(row => sharedValues.Contains(row.Value))
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return [.. sharedKeys.Select(key => new DuplicateRoots
            {
                Source = key.Source,
                Value = key.Value,
                Items = rows
                    .Where(row => row.ContentType == key.ContentType && row.Level == key.Level &&
                        row.Source == key.Source && row.Value == key.Value)
                    .DistinctBy(row => row.ItemId)
                    .ToDictionary(row => row.ItemId, row => row.ItemName)
            })];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The hierarchy item repository failed to find duplicate roots");
            throw;
        }
    }

    public async Task RecomputeAllEffectiveValuesAsync(CancellationToken cancellationToken = default)
    {
        var recompute = new EffectiveRecompute(
            new Dictionary<Guid, IReadOnlyCollection<string>>(),
            new Dictionary<Guid, EffectiveNode>());
        await transactionManager.ExecuteWithTransactionAsync(
            async _ =>
            {
                recompute = await RecomputeEffectiveValuesAsync(
                    new EffectiveValueTrigger(EffectiveValueTriggerKind.Full),
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        await PublishEffectiveChangesAsync(recompute, cancellationToken).ConfigureAwait(false);
    }

    private static HashSet<Guid> ResolveTargetIds(
        EffectiveValueTrigger trigger,
        IReadOnlyList<EffectiveNode> nodes,
        IReadOnlyDictionary<Guid, List<Guid>> childrenByParent,
        IReadOnlyDictionary<Guid, EffectiveValues> resolved)
    {
        if (trigger.Kind == EffectiveValueTriggerKind.Full)
        {
            return [.. nodes.Select(node => node.Id)];
        }

        if (trigger.Kind == EffectiveValueTriggerKind.ItemSubtree && trigger.ItemId is { } itemId)
        {
            return ListSubtreeIds(itemId, childrenByParent);
        }

        if (trigger.Kind == EffectiveValueTriggerKind.Reparent)
        {
            var ids = new HashSet<Guid>();
            if (trigger.OldParentId is { } oldParentId)
            {
                ids.UnionWith(ListSubtreeIds(oldParentId, childrenByParent));
            }

            if (trigger.NewParentId is { } newParentId)
            {
                ids.UnionWith(ListSubtreeIds(newParentId, childrenByParent));
            }

            if (trigger.ItemId is { } reparentedItemId)
            {
                ids.UnionWith(ListSubtreeIds(reparentedItemId, childrenByParent));
            }

            return ids;
        }

        if (trigger.Kind == EffectiveValueTriggerKind.RootFolder)
        {
            return [.. nodes
                .Where(node => node.EffectiveRootFolderId == trigger.RootFolderId ||
                    string.Equals(node.ContentType, trigger.ContentType, StringComparison.Ordinal))
                .Select(node => node.Id)];
        }

        if (trigger.Kind == EffectiveValueTriggerKind.QualityTierList)
        {
            return [.. resolved
                .Where(entry => entry.Value.UsesLevelDefaultQualityTierList)
                .Select(entry => entry.Key)];
        }

        return [.. resolved
            .Where(entry => entry.Value.UsesGlobalMonitoring)
            .Select(entry => entry.Key)];
    }

    private static HashSet<Guid> ListSubtreeIds(Guid rootId, IReadOnlyDictionary<Guid, List<Guid>> childrenByParent)
    {
        var ids = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(rootId);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!ids.Add(current) || !childrenByParent.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                pending.Push(child);
            }
        }

        return ids;
    }

    private async Task<EffectiveRecompute> RecomputeEffectiveValuesAsync(
        EffectiveValueTrigger trigger,
        CancellationToken cancellationToken)
    {
        var nodes = await ListEffectiveNodesAsync(trigger, cancellationToken).ConfigureAwait(false);
        if (nodes.Count == 0)
        {
            return new(new Dictionary<Guid, IReadOnlyCollection<string>>(), new Dictionary<Guid, EffectiveNode>());
        }

        var rootFolders = await context.RootFolders
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var globalMonitoring = await ResolveGlobalMonitoringAsync(cancellationToken).ConfigureAwait(false);
        var nodesById = nodes.ToDictionary(node => node.Id);
        var childrenByParent = (from node in nodes where node.ParentId is not null group node by node.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(node => node.Id).ToList());
        var resolved = nodes.ToDictionary(
            node => node.Id,
            node => ResolveEffectiveValues(
                node,
                nodesById,
                rootFolders,
                globalMonitoring,
                contentTypeRegistry));
        var targetIds = ResolveTargetIds(trigger, nodes, childrenByParent, resolved);
        var changed = new Dictionary<Guid, IReadOnlyCollection<string>>();
        var updates = new List<(Guid Id, EffectiveValues Values)>();

        foreach (var node in nodes.Where(node => targetIds.Contains(node.Id)))
        {
            var values = resolved[node.Id];
            var effectiveRootFolderId = values.RootFolderId ?? node.EffectiveRootFolderId;
            var effectiveQualityTierListId = values.QualityTierListId;
            var fields = new List<string>();
            if (node.EffectiveMonitoring != values.Monitoring)
            {
                fields.Add(nameof(ContentHierarchyItem.EffectiveMonitoring));
            }

            if (node.EffectiveRootFolderId != effectiveRootFolderId)
            {
                fields.Add(nameof(ContentHierarchyItem.EffectiveRootFolderId));
            }

            if (node.EffectiveQualityTierListId != effectiveQualityTierListId)
            {
                fields.Add(nameof(ContentHierarchyItem.EffectiveQualityTierListId));
            }

            if (fields.Count == 0)
            {
                continue;
            }

            changed[node.Id] = fields;
            updates.Add((
                node.Id,
                values with
                {
                    RootFolderId = effectiveRootFolderId,
                    QualityTierListId = effectiveQualityTierListId
                }));
        }

        foreach (var group in updates.GroupBy(update => update.Values))
        {
            var ids = group.Select(update => update.Id).ToArray();
            var values = group.Key;
            await context.ContentHierarchyItems
                .Where(item => ids.Contains(item.Id))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.EffectiveMonitoring, values.Monitoring)
                        .SetProperty(item => item.EffectiveRootFolderId, values.RootFolderId!.Value)
                        .SetProperty(
                            item => item.EffectiveQualityTierListId,
                            values.QualityTierListId),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var (id, values) in updates)
        {
            nodesById[id] = nodesById[id] with
            {
                EffectiveMonitoring = values.Monitoring,
                EffectiveRootFolderId = values.RootFolderId!.Value,
                EffectiveQualityTierListId = values.QualityTierListId
            };
        }

        return new(changed, nodesById);
    }

    public async Task<bool> TryCreateAsync(
        ContentHierarchyItem model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction;
        var savepointTaken = transaction is { SupportsSavepoints: true };
        try
        {
            if (savepointTaken)
            {
                await transaction!.CreateSavepointAsync(CreateSavepoint, cancellationToken).ConfigureAwait(false);
            }

            await CreateCoreAsync(model, createdByUserId, cancellationToken).ConfigureAwait(false);
            if (savepointTaken)
            {
                await transaction!.ReleaseSavepointAsync(CreateSavepoint, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (DbUpdateException ex)
        {
            context.Entry(model).State = EntityState.Detached;
            if (model.MetadataContainer is not null)
            {
                context.Entry(model.MetadataContainer).State = EntityState.Detached;
            }

            if (savepointTaken)
            {
                await transaction!.RollbackToSavepointAsync(CreateSavepoint, cancellationToken)
                    .ConfigureAwait(false);
                await transaction.ReleaseSavepointAsync(CreateSavepoint, cancellationToken).ConfigureAwait(false);
            }

            if (!IsHierarchyIdentityUniqueViolation(ex))
            {
                logger.LogError(
                    ex,
                    "The hierarchy item repository failed to create item {ContentHierarchyItemId}",
                    model.Id);
                throw;
            }

            logger.LogWarning(
                ex,
                "The hierarchy item already exists for content type {ContentType}, level {Level}, parent " +
                "{ParentId}, and slug {Slug}",
                model.ContentType,
                model.Level,
                model.ParentId,
                model.Slug);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The hierarchy item repository failed to create item {ContentHierarchyItemId}",
                model.Id);
            throw;
        }
    }

    private static bool IsHierarchyIdentityUniqueViolation(Exception exception)
    {
        for (var current = (Exception?)exception; current is not null; current = current.InnerException)
        {
            if (current is not DbException databaseException)
            {
                continue;
            }

            var message = databaseException.Message;
            var hierarchyIdentity = message.Contains(
                    "IX_content_hierarchyItem_ContentType_Level_Slug",
                    StringComparison.Ordinal) ||
                message.Contains(
                    "IX_content_hierarchyItem_ContentType_ParentId_Level_Slug",
                    StringComparison.Ordinal) ||
                message.Contains("content_hierarchyItem.ContentType", StringComparison.Ordinal) &&
                message.Contains("content_hierarchyItem.Level", StringComparison.Ordinal) &&
                message.Contains("content_hierarchyItem.Slug", StringComparison.Ordinal);
            if (!hierarchyIdentity)
            {
                continue;
            }

            if (databaseException.SqlState == "23505")
            {
                return true;
            }

            if (databaseException.GetType().FullName == "Microsoft.Data.SqlClient.SqlException" &&
                databaseException.GetType().GetProperty("Number")?.GetValue(databaseException) is 2601 or 2627)
            {
                return true;
            }

            if (databaseException.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException" &&
                databaseException.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(databaseException)
                    is 2067)
            {
                return true;
            }
        }

        return false;
    }

    public async Task<ContentStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var total = await context.ContentHierarchyItems
                .AsNoTracking()
                .CountAsync(cancellationToken)
                .ConfigureAwait(false);

            var groups = await context.ContentHierarchyItems
                .AsNoTracking()
                .GroupBy(i => i.ContentItem != null ? (FulfillmentStatus?)i.ContentItem.Fulfillment : null)
                .Select(g => new { Fulfillment = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var missing = 0;
            var upgradable = 0;
            var fulfilled = 0;
            var fulfilledExternally = 0;
            var partial = 0;

            foreach (var group in groups)
            {
                if (group.Fulfillment is null)
                {
                    missing += group.Count;
                    continue;
                }

                switch (group.Fulfillment.Value)
                {
                    case FulfillmentStatus.Missing:
                        missing += group.Count;
                        break;
                    case FulfillmentStatus.Upgradable:
                        upgradable += group.Count;
                        break;
                    case FulfillmentStatus.Fulfilled:
                        fulfilled += group.Count;
                        break;
                    case FulfillmentStatus.FulfilledExternally:
                        fulfilledExternally += group.Count;
                        break;
                    case FulfillmentStatus.Partial:
                        partial += group.Count;
                        break;
                }
            }

            return new()
            {
                Total = total,
                Missing = missing,
                Upgradable = upgradable,
                Fulfilled = fulfilled,
                FulfilledExternally = fulfilledExternally,
                Partial = partial
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The hierarchy item repository failed to compute the content stats.");
            throw;
        }
    }

    public async Task<PagedList<ContentHierarchyItem>> QueryAsync(
        ContentHierarchyItemCriteria query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var items = context.ContentHierarchyItems.AsNoTracking();

            items = ApplyCriteria(items, query);
            items = await ApplyAncestorFilterAsync(items, query, cancellationToken).ConfigureAwait(false);

            var totalCount = await items.CountAsync(cancellationToken).ConfigureAwait(false);

            items = ApplySort(context.Model, items, sort);

            var pageIds = await items
                .Skip((paging.PageNumber - 1) * paging.PageSize)
                .Take(paging.PageSize)
                .Select(item => item.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var hydratedItems = await IncludeReadModel(context.ContentHierarchyItems.AsNoTracking())
                .Where(item => pageIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken)
                .ConfigureAwait(false);
            var result = pageIds
                .Select(id => hydratedItems.GetValueOrDefault(id))
                .OfType<ContentHierarchyItem>()
                .ToList();
            OrderMediaFiles(result);
            await MarkImportPendingAsync(result, cancellationToken).ConfigureAwait(false);
            await LoadTagNamesAsync(result, cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = result,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalItems = totalCount
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The hierarchy item repository failed to query the items.");
            throw;
        }
    }

    public async Task<IReadOnlyList<Guid>> QueryIdsAsync(
        ContentHierarchyItemCriteria query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var items = ApplyCriteria(context.ContentHierarchyItems.AsNoTracking(), query);
            items = await ApplyAncestorFilterAsync(items, query, cancellationToken).ConfigureAwait(false);

            return await ApplySort(context.Model, items, sort)
                .Skip((paging.PageNumber - 1) * paging.PageSize)
                .Take(paging.PageSize)
                .Select(item => item.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The hierarchy item repository failed to query the item ids.");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<(string ContentType, string Level, string Source, string Value), Guid>>
        FindByExternalIdentitiesAsync(
            IReadOnlyCollection<(string ContentType, string Level, string Source, string Value)> identities,
            CancellationToken cancellationToken = default)
    {
        try
        {
            var matches = new Dictionary<(string ContentType, string Level, string Source, string Value), Guid>();
            var scopes = identities
                .Where(identity => !string.IsNullOrWhiteSpace(identity.ContentType) &&
                    !string.IsNullOrWhiteSpace(identity.Level) &&
                    !string.IsNullOrWhiteSpace(identity.Source) &&
                    !string.IsNullOrWhiteSpace(identity.Value))
                .Select(identity => (
                    ContentType: identity.ContentType.ToLowerInvariant(),
                    Level: identity.Level.ToLowerInvariant(),
                    Source: identity.Source.ToLowerInvariant(),
                    Value: identity.Value.ToLowerInvariant()))
                .Distinct()
                .GroupBy(identity => (identity.ContentType, identity.Level, identity.Source));
            foreach (var scope in scopes)
            {
                foreach (var values in scope.Select(identity => identity.Value).Chunk(500))
                {
                    // A collection parameter avoids expensive scalar IN predicates on SQL Server.
                    var matchingContainers =
                        from contribution in context.MetadataContributions.AsNoTracking()
                        from externalId in contribution.ExternalIds
                        where externalId.Source != null && externalId.Source.ToLower() == scope.Key.Source &&
                            EF.Parameter(values).Contains(externalId.Value.ToLower())
                        select contribution.MetadataContainerId;
                    var rows = await (
                        from item in context.ContentHierarchyItems.AsNoTracking()
                        join contribution in context.MetadataContributions.AsNoTracking()
                            on item.MetadataContainerId equals contribution.MetadataContainerId
                        from externalId in contribution.ExternalIds
                        where item.ParentId == null && item.ContentType.ToLower() == scope.Key.ContentType &&
                            item.Level.ToLower() == scope.Key.Level && externalId.Source != null &&
                            externalId.Source.ToLower() == scope.Key.Source &&
                            matchingContainers.Contains(item.MetadataContainerId)
                        orderby item.CreatedAt, item.Id, contribution.Ordinal, contribution.Id, externalId.Ordinal
                        select new { item.Id, ContributionId = contribution.Id, externalId.Value })
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    var requestedValues = values.ToHashSet(StringComparer.Ordinal);
                    foreach (var itemRows in rows.GroupBy(row => row.Id))
                    {
                        // Keep the database's contribution-id ordering for equal contribution ordinals.
                        var effectiveContributionId = itemRows.First().ContributionId;
                        foreach (var row in itemRows.Where(row => row.ContributionId == effectiveContributionId))
                        {
                            var value = row.Value.ToLowerInvariant();
                            if (requestedValues.Contains(value))
                            {
                                matches.TryAdd(
                                    (scope.Key.ContentType, scope.Key.Level, scope.Key.Source, value), row.Id);
                            }
                        }
                    }
                }
            }

            return matches;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The hierarchy item repository failed to match external identities.");
            throw;
        }
    }

    private static void OrderMediaFiles(IEnumerable<ContentHierarchyItem> items)
    {
        foreach (var mediaFiles in items
            .Select(item => item.ContentItem?.MediaFiles)
            .OfType<ICollection<MediaFile>>())
        {
            var ordered = mediaFiles.OrderBy(file => file.FilePath, MediaFilePathComparer).ToList();
            mediaFiles.Clear();
            foreach (var file in ordered)
            {
                mediaFiles.Add(file);
            }
        }
    }

    public async Task<ContentHierarchyItem> CreateAsync(
        ContentHierarchyItem model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await CreateCoreAsync(model, createdByUserId, cancellationToken).ConfigureAwait(false);
            return model;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The hierarchy item repository failed to create item " +
                "{ContentHierarchyItemName}.", model.Name);
            throw;
        }
    }

    private async Task CreateCoreAsync(
        ContentHierarchyItem model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        var nodes = new Dictionary<Guid, EffectiveNode>();
        await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var now = _time.GetUtcNow().UtcDateTime;
                model.CreatedAt = now;
                model.CreatedByUserId = createdByUserId;

                if (model.ParentId is { } parentId)
                {
                    var lineage = await GetLineageAsync(parentId, ct).ConfigureAwait(false);
                    if (lineage.Count > 0)
                    {
                        await LockAsync(lineage[^1].Id, ct).ConfigureAwait(false);
                    }
                }

                nodes = await PrepareEffectiveValuesForCreateAsync(model, ct).ConfigureAwait(false);
                context.ContentHierarchyItems.Add(model);
                await context.SaveChangesAsync(ct).ConfigureAwait(false);

                await contentRepository.ReconcileMediaStateAsync(model.Id, createdByUserId, ct)
                    .ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(
                    new()
                    {
                        Category = AuditCategories.Content,
                        Operation = "Create",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(ContentHierarchyItem),
                        Detail = $"Created content item {model.Name}.",
                        Timestamp = now,
                        UserId = createdByUserId
                    },
                    ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        await transactionManager.OnCommittedAsync(
            token => eventBus.PublishAsync(
                ContentEventBuilder.BuildContentHierarchyItemCreatedEvent(ImpactOf(model.Id, nodes)),
                token),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UpdateAsync(
        ContentHierarchyItem model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var publishedChanges = new Dictionary<Guid, HashSet<string>>();
            var nodes = new Dictionary<Guid, EffectiveNode>();
            var scopes = new Dictionary<Guid, ScopeChange>();
            var applied = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var current = await context.ContentHierarchyItems
                        .AsNoTracking()
                        .FirstOrDefaultAsync(item => item.Id == model.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (current is null)
                    {
                        return false;
                    }

                    var oldLineage = await GetLineageAsync(model.Id, cancellationToken).ConfigureAwait(false);
                    var newLineage = model.ParentId is { } nextParent
                        ? await GetLineageAsync(nextParent, cancellationToken).ConfigureAwait(false) : [];
                    var roots = oldLineage.TakeLast(1).Concat(newLineage.TakeLast(1))
                        .Select(item => item.Id).Distinct().Order();
                    foreach (var root in roots)
                    {
                        await LockAsync(root, cancellationToken).ConfigureAwait(false);
                    }

                    var lockedCurrent = await context.ContentHierarchyItems.AsNoTracking()
                        .SingleOrDefaultAsync(item => item.Id == model.Id, cancellationToken).ConfigureAwait(false);
                    if (lockedCurrent is null)
                    {
                        return false;
                    }

                    if (lockedCurrent.ParentId != current.ParentId)
                    {
                        throw new DbUpdateConcurrencyException("The content hierarchy changed before it could move.");
                    }

                    var updated = await context.ContentHierarchyItems
                        .Where(i => i.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(i => i.ParentId, model.ParentId)
                                .SetProperty(i => i.QualityTierListId, model.QualityTierListId)
                                .SetProperty(i => i.RootFolderId, model.RootFolderId)
                                .SetProperty(i => i.ContentType, model.ContentType)
                                .SetProperty(i => i.Level, model.Level)
                                .SetProperty(i => i.Slug, model.Slug)
                                .SetProperty(i => i.Name, model.Name)
                                .SetProperty(i => i.Monitoring, model.Monitoring)
                                .SetProperty(i => i.Path, model.Path)
                                .SetProperty(i => i.LanguagePreferences, model.LanguagePreferences)
                                .SetProperty(i => i.Settings, model.Settings)
                                .SetProperty(i => i.DateLastRefreshed, model.DateLastRefreshed)
                                .SetProperty(i => i.PendingMetadataRefresh, model.PendingMetadataRefresh)
                                .SetProperty(i => i.ModifiedAt, now)
                                .SetProperty(i => i.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Content,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(ContentHierarchyItem),
                                Detail = $"Updated content item {model.Name}.",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);

                        MergeChanges(publishedChanges, model.Id, ChangedFields(current, model));
                        scopes[model.Id] = new(
                            null,
                            new()
                            {
                                MetadataContainerId = current.MetadataContainerId,
                                ParentId = current.ParentId,
                                ContentType = current.ContentType,
                                Level = current.Level,
                                TopLevel = current.ParentId is null,
                                RootFolderId = current.RootFolderId,
                                QualityTierListId = current.QualityTierListId,
                                Monitoring = current.EffectiveMonitoring != MonitoringMode.Unmonitored
                            });
                        var trigger = current.ParentId != model.ParentId
                            ? new EffectiveValueTrigger(
                                EffectiveValueTriggerKind.Reparent,
                                model.Id,
                                current.ParentId,
                                model.ParentId)
                            : new EffectiveValueTrigger(EffectiveValueTriggerKind.ItemSubtree, ItemId: model.Id);
                        var recompute = await RecomputeEffectiveValuesAsync(trigger, cancellationToken)
                            .ConfigureAwait(false);
                        nodes = new(recompute.Nodes);
                        MergeChanges(publishedChanges, recompute.Changes);
                        await contentRepository.ReconcileMediaStateAsync(model.Id, modifiedByUserId,
                            cancellationToken).ConfigureAwait(false);
                        if (current.ParentId is { } priorParent && current.ParentId != model.ParentId)
                        {
                            await contentRepository.ReconcileMediaStateAsync(priorParent, modifiedByUserId,
                                cancellationToken).ConfigureAwait(false);
                        }
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (applied)
            {
                await transactionManager.OnCommittedAsync(
                    token => PublishChangesAsync(publishedChanges, nodes, scopes, token),
                    cancellationToken).ConfigureAwait(false);
            }

            return applied;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The hierarchy item repository failed to update item " +
                "{ContentHierarchyItemId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken = default)
    {
        var deletedIds = await DeleteSubtreeAsync(id, deletedByUserId, cancellationToken).ConfigureAwait(false);
        return deletedIds.Count > 0;
    }

    public async Task<IReadOnlyList<Guid>> DeleteSubtreeAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var impact = new ContentEventImpact { HierarchyItemId = id };
            var removed = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var lineage = await GetLineageAsync(id, cancellationToken).ConfigureAwait(false);
                    if (lineage.Count > 0)
                    {
                        await LockAsync(lineage[^1].Id, cancellationToken).ConfigureAwait(false);
                    }

                    var model = await (
                        from i in context.ContentHierarchyItems.AsNoTracking()
                        where i.Id == id
                        select new
                        {
                            i.Name,
                            i.ParentId,
                            i.ContentType,
                            i.Level,
                            i.MetadataContainerId,
                            i.RootFolderId,
                            i.QualityTierListId,
                            i.EffectiveMonitoring
                        })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return [];
                    }

                    var subtreeIds = await RemoveSubtreeAsync(id, cancellationToken).ConfigureAwait(false);
                    if (model.ParentId is { } parentId)
                    {
                        await contentRepository.ReconcileMediaStateAsync(parentId, deletedByUserId,
                            cancellationToken).ConfigureAwait(false);
                    }

                    impact = new()
                    {
                        HierarchyItemId = id,
                        MetadataContainerId = model.MetadataContainerId,
                        ParentId = model.ParentId,
                        ContentType = model.ContentType,
                        Level = model.Level,
                        TopLevel = model.ParentId is null,
                        RootFolderId = model.RootFolderId,
                        QualityTierListId = model.QualityTierListId,
                        Monitoring = model.EffectiveMonitoring != MonitoringMode.Unmonitored,
                        SubtreeIds = subtreeIds
                    };

                    if (subtreeIds.Count > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Content,
                                Operation = "Delete",
                                EntityId = id.ToString(),
                                EntityType = nameof(ContentHierarchyItem),
                                Detail = $"Deleted content item {model.Name}.",
                                Timestamp = now,
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return subtreeIds;
                },
                cancellationToken).ConfigureAwait(false);

            if (removed.Count > 0)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ContentEventBuilder.BuildContentHierarchyItemDeletedEvent(impact),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The hierarchy item repository failed to delete item {ContentHierarchyItemId}.", id);
            throw;
        }
    }

    private static IQueryable<ContentHierarchyItem> ApplyCollectionCriteria<TCriteria>(
        IQueryable<ContentHierarchyItem> query,
        CollectionCriteria<TCriteria> criteria,
        Func<TCriteria, IQueryable<Guid>> matchingContainerIds)
    {
        foreach (var group in criteria.Some)
        {
            if (group.Count == 0)
            {
                continue;
            }

            var groupContainerIds = ConcatBalanced(group.Select(matchingContainerIds).ToList());

            if (groupContainerIds is not null)
            {
                query = query.Where(i => groupContainerIds.Contains(i.MetadataContainerId));
            }
        }

        foreach (var none in criteria.None)
        {
            var noneContainerIds = matchingContainerIds(none);
            query = query.Where(i => !noneContainerIds.Contains(i.MetadataContainerId));
        }

        return query;
    }

    private static List<string>? ExactExternalIdKeys(IReadOnlyList<ExternalIdCriteria> group)
    {
        var keys = new List<string>(group.Count);
        foreach (var alternative in group)
        {
            var source = ExactValue(alternative.Source.And);
            var value = ExactValue(alternative.Value.And);
            if (source is null || value is null)
            {
                return null;
            }

            keys.Add((source + ExternalIdKeySeparator + value).ToLowerInvariant());
        }

        return keys.Count == 0 ? null : keys;
    }

    private static string? ExactValue(IEnumerable<PropertyCriterion<string>> criteria)
    {
        var list = criteria.ToList();
        return list is [{ } criterion] &&
            !criterion.Negate &&
            criterion.Operator == CriteriaValueOperator.Equals &&
            !string.IsNullOrEmpty(criterion.Value) &&
            (criterion.Values is null || !criterion.Values.Any())
                ? criterion.Value
                : null;
    }

    private static IQueryable<Guid>? ConcatBalanced(IReadOnlyList<IQueryable<Guid>> parts)
    {
        return parts.Count == 0 ? null : ConcatParts(parts);
    }

    private static IQueryable<Guid> ConcatParts(IReadOnlyList<IQueryable<Guid>> parts)
    {
        if (parts.Count == 1)
        {
            return parts[0];
        }

        var middle = parts.Count / 2;
        return ConcatParts([.. parts.Take(middle)]).Concat(ConcatParts([.. parts.Skip(middle)]));
    }

    private async Task<IReadOnlyList<Guid>> ResolveMatchingIdsAsync(
        ContentHierarchyItemCriteria criteria,
        CancellationToken cancellationToken)
    {
        var items = ApplyCriteria(context.ContentHierarchyItems.AsNoTracking(), criteria);

        items = await ApplyAncestorFilterAsync(items, criteria, cancellationToken).ConfigureAwait(false);

        return await items
            .Select(i => i.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IQueryable<ContentHierarchyItem>> ApplyAncestorFilterAsync(
        IQueryable<ContentHierarchyItem> query,
        ContentHierarchyItemCriteria criteria,
        CancellationToken cancellationToken)
    {
        if (criteria.AncestorId is not { } ancestorId)
        {
            return query;
        }

        var descendantIds = await ListDescendantIdsAsync(ancestorId, cancellationToken).ConfigureAwait(false);
        return query.Where(i => descendantIds.Contains(i.Id));
    }

    private async Task<HashSet<Guid>> ListDescendantIdsAsync(Guid ancestorId, CancellationToken cancellationToken)
    {
        var descendantIds = new HashSet<Guid>();
        var currentLevel = new List<Guid> { ancestorId };
        while (currentLevel.Count > 0)
        {
            var parentIds = currentLevel;
            var childIds = await (
                from i in context.ContentHierarchyItems.AsNoTracking()
                where i.ParentId != null && parentIds.Contains(i.ParentId.Value)
                select i.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            currentLevel = [.. childIds.Where(descendantIds.Add)];
        }

        return descendantIds;
    }

    public async Task<(BulkResult Result, IReadOnlyList<Guid> DeletedIds)> BulkDeleteAsync(
        BulkAction<ContentHierarchyItemCriteria> action,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var targetIds = await ResolveBulkTargetIdsAsync(action.Ids, action.Filter, cancellationToken)
                .ConfigureAwait(false);
            if (targetIds.Count == 0)
            {
                return (new BulkResult { Count = 0 }, []);
            }

            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var existingIds = await (
                        from i in context.ContentHierarchyItems.AsNoTracking()
                        where targetIds.Contains(i.Id)
                        select i.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    var deletedCount = 0;
                    var deletedIds = new List<Guid>();
                    foreach (var id in existingIds)
                    {
                        var removed = await RemoveSubtreeAsync(id, cancellationToken).ConfigureAwait(false);
                        deletedIds.AddRange(removed);
                        deletedCount += removed.Count > 0 ? 1 : 0;
                    }

                    if (deletedCount > 0)
                    {
                        var deletedRootCountLabel = deletedCount == 1
                            ? "1 content item"
                            : $"{deletedCount} content items";
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Content,
                                Operation = "BulkDelete",
                                EntityType = nameof(ContentHierarchyItem),
                                Detail = $"Deleted {deletedRootCountLabel} and all of their descendants.",
                                Timestamp = now,
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return (new BulkResult { Count = deletedCount }, (IReadOnlyList<Guid>)deletedIds);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The hierarchy item repository failed to bulk-delete the items.");
            throw;
        }
    }

    public async Task<BulkResult> BulkPatchAsync(
        BulkPatch<ContentHierarchyItemCriteria, ContentHierarchyItemPatch> patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var targetIds = await ResolveBulkTargetIdsAsync(patch.Ids, patch.Filter, cancellationToken)
                .ConfigureAwait(false);
            if (targetIds.Count == 0)
            {
                return new BulkResult { Count = 0 };
            }

            var patchedIds = new List<Guid>();
            var publishedChanges = new Dictionary<Guid, HashSet<string>>();
            var nodes = new Dictionary<Guid, EffectiveNode>();
            var scopes = new Dictionary<Guid, ScopeChange>();
            var result = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    foreach (var id in targetIds)
                    {
                        var outcome = await ApplyPatchAsync(
                            id,
                            patch.Patch,
                            modifiedByUserId,
                            now,
                            publishedChanges,
                            nodes,
                            cancellationToken)
                            .ConfigureAwait(false);
                        if (outcome.Applied)
                        {
                            patchedIds.Add(id);
                            scopes[id] = new(outcome.Current, outcome.Previous);
                            MergeChanges(publishedChanges, id, ChangedFields(patch.Patch));
                        }
                    }

                    return new BulkResult { Count = patchedIds.Count };
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => PublishChangesAsync(publishedChanges, nodes, scopes, token),
                cancellationToken).ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The hierarchy item repository failed to bulk-patch the items.");
            throw;
        }
    }

    private static Uri? ParseAbsoluteUri(string? value)
    {
        if (value == null)
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"Image URL \"{value}\" must be an absolute URL.", nameof(value));
        }

        return uri;
    }

    private async Task<IReadOnlyList<Guid>> ResolveBulkTargetIdsAsync(
        IReadOnlyList<Guid>? ids,
        ContentHierarchyItemCriteria? filter,
        CancellationToken cancellationToken)
    {
        var targetIds = new HashSet<Guid>();
        if (ids is not null)
        {
            foreach (var id in ids)
            {
                targetIds.Add(id);
            }
        }

        if (filter is not null)
        {
            foreach (var id in await ResolveMatchingIdsAsync(filter, cancellationToken).ConfigureAwait(false))
            {
                targetIds.Add(id);
            }
        }

        return [.. targetIds];
    }

    private async Task<IReadOnlyList<Guid>> RemoveSubtreeAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var lineage = await GetLineageAsync(id, cancellationToken).ConfigureAwait(false);
        if (lineage.Count > 0)
        {
            await LockAsync(lineage[^1].Id, cancellationToken).ConfigureAwait(false);
        }

        var rootExists = await context.ContentHierarchyItems
            .AsNoTracking()
            .AnyAsync(i => i.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (!rootExists)
        {
            return [];
        }

        var currentLevel = new List<Guid> { id };
        var levels = new List<List<Guid>> { currentLevel };
        while (currentLevel.Count > 0)
        {
            var parentIds = currentLevel;
            var childIds = await (
                from i in context.ContentHierarchyItems.AsNoTracking()
                where i.ParentId != null && parentIds.Contains(i.ParentId.Value)
                select i.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (childIds.Count == 0)
            {
                break;
            }

            levels.Add(childIds);
            currentLevel = childIds;
        }

        var subtreeIds = levels.SelectMany(level => level).ToList();

        var containerIds = await (
            from i in context.ContentHierarchyItems.AsNoTracking()
            where subtreeIds.Contains(i.Id)
            select i.MetadataContainerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        await context.DownloadScopes
            .Where(c => subtreeIds.Contains(c.ContentHierarchyItemId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await context.ReleaseScopes
            .Where(c => subtreeIds.Contains(c.ContentHierarchyItemId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await context.MediaFiles
            .Where(f => context.ContentItems
                .Where(i => subtreeIds.Contains(i.ContentHierarchyItemId))
                .Select(i => i.Id)
                .Contains(f.ContentItemId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await context.ContentItems
            .Where(i => subtreeIds.Contains(i.ContentHierarchyItemId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        for (var depth = levels.Count - 1; depth >= 0; depth--)
        {
            var levelIds = levels[depth];
            await context.ContentHierarchyItems
                .Where(i => levelIds.Contains(i.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await metadataContainerRepository.RemoveContainersAsync(containerIds, cancellationToken).ConfigureAwait(false);

        if (lineage.Count > 1)
        {
            await contentRepository.ReconcileMediaStateAsync(lineage[1].Id, null, cancellationToken)
                .ConfigureAwait(false);
        }

        return subtreeIds;
    }

    public async Task<bool> PatchAsync(
        ContentHierarchyItemPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var publishedChanges = new Dictionary<Guid, HashSet<string>>();
            var nodes = new Dictionary<Guid, EffectiveNode>();
            var scopes = new Dictionary<Guid, ScopeChange>();
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var outcome = await ApplyPatchAsync(
                        patch.Id,
                        patch,
                        modifiedByUserId,
                        now,
                        publishedChanges,
                        nodes,
                        cancellationToken)
                        .ConfigureAwait(false);
                    scopes[patch.Id] = new(outcome.Current, outcome.Previous);
                    return outcome.Applied;
                },
                cancellationToken).ConfigureAwait(false);

            if (patched)
            {
                MergeChanges(publishedChanges, patch.Id, ChangedFields(patch));
                await transactionManager.OnCommittedAsync(
                    token => PublishChangesAsync(publishedChanges, nodes, scopes, token),
                    cancellationToken).ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The hierarchy item repository failed to patch item " +
                "{ContentHierarchyItemId}.", patch.Id);
            throw;
        }
    }

    private static Expression<Func<TItem, Guid>> ContributionKeySelector<TItem>()
    {
        var parameter = Expression.Parameter(typeof(TItem), "i");
        var member = Expression.Property(parameter, nameof(Genre.MetadataContributionId));
        return Expression.Lambda<Func<TItem, Guid>>(member, parameter);
    }

    private static IQueryable<Image> ApplyImageUrlCriteria(
        IQueryable<Image> query,
        CriteriaSet<PropertyCriterion<string>> criteria)
    {
        if (criteria.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var criterion in criteria.And)
        {
            var uri = ParseAbsoluteUri(criterion.Value);
            var uris = (criterion.Values ?? [])
                .Where(v => v != null)
                .Select(v => ParseAbsoluteUri(v)!)
                .ToList();
            query = criterion.Operator switch
            {
                CriteriaValueOperator.Equals => query.Where(i => uri != null && i.Url == uri),
                CriteriaValueOperator.NotEquals => query.Where(i => uri == null || i.Url != uri),
                CriteriaValueOperator.IsNull => query.Where(i => i.Url == null),
                CriteriaValueOperator.IsNotNull => query.Where(i => i.Url != null),
                CriteriaValueOperator.In => query.Where(i => uris.Contains(i.Url)),
                CriteriaValueOperator.NotIn => query.Where(i => !uris.Contains(i.Url)),
                var op => throw new ArgumentException(
                    $"Operator {op} is not supported for image URL.", nameof(criteria))
            };
        }

        return query;
    }

    private static IQueryable<ContentHierarchyItem> ApplyMetadataScalarCriteria(
        IQueryable<ContentHierarchyItem> query,
        MetadataCriteria metadata)
    {
        query = ApplyEffectiveTemporalCriteria(
            query,
            metadata.PublishDate,
            i => i.MetadataContainer == null
                ? null
                : i.MetadataContainer.Contributions.Where(c => c.PublishDate != null)
                    .OrderBy(c => c.Ordinal).Select(c => c.PublishDate).FirstOrDefault());
        query = ApplyEffectiveTemporalCriteria(
            query,
            metadata.Established,
            i => i.MetadataContainer == null
                ? null
                : i.MetadataContainer.Contributions.Where(c => c.Established != null)
                    .OrderBy(c => c.Ordinal).Select(c => c.Established).FirstOrDefault());
        query = ApplyEffectiveTemporalCriteria(
            query,
            metadata.Dissolved,
            i => i.MetadataContainer == null
                ? null
                : i.MetadataContainer.Contributions.Where(c => c.Dissolved != null)
                    .OrderBy(c => c.Ordinal).Select(c => c.Dissolved).FirstOrDefault());
        query = ApplyEffectiveTemporalCriteria(
            query,
            metadata.BirthDate,
            i => i.MetadataContainer == null
                ? null
                : i.MetadataContainer.Contributions.Where(c => c.BirthDate != null)
                    .OrderBy(c => c.Ordinal).Select(c => c.BirthDate).FirstOrDefault());
        query = ApplyEffectiveTemporalCriteria(
            query,
            metadata.DeathDate,
            i => i.MetadataContainer == null
                ? null
                : i.MetadataContainer.Contributions.Where(c => c.DeathDate != null)
                    .OrderBy(c => c.Ordinal).Select(c => c.DeathDate).FirstOrDefault());
        query = ApplyEffectiveComparableCriteria(
            query,
            metadata.Duration,
            i => i.MetadataContainer == null
                ? null
                : i.MetadataContainer.Contributions.Where(c => c.Duration != null)
                    .OrderBy(c => c.Ordinal).Select(c => c.Duration).FirstOrDefault());
        query = ApplyEffectiveStringCriteria(
            query,
            metadata.Language,
            i => i.MetadataContainer == null
                ? null
                : i.MetadataContainer.Contributions.Where(c => c.Language != null)
                    .OrderBy(c => c.Ordinal).Select(c => c.Language).FirstOrDefault());
        query = ApplyEffectiveStringCriteria(
            query,
            metadata.ProductionStatus,
            i => i.MetadataContainer == null
                ? null
                : i.MetadataContainer.Contributions.Where(c => c.ProductionStatus != null)
                    .OrderBy(c => c.Ordinal).Select(c => c.ProductionStatus).FirstOrDefault());

        return query;
    }

    private static IQueryable<ContentHierarchyItem> ApplyEffectiveTemporalCriteria(
        IQueryable<ContentHierarchyItem> query,
        CriteriaSet<PropertyCriterion<TemporalValue>> criteria,
        Expression<Func<ContentHierarchyItem, DateTime?>> effective)
    {
        if (criteria.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var criterion in criteria.And)
        {
            var predicate = effective.Body.BuildTemporalPredicateBody(criterion);
            query = query.Where(Expression.Lambda<Func<ContentHierarchyItem, bool>>(predicate, effective.Parameters));
        }

        return query;
    }

    private static IQueryable<ContentHierarchyItem> ApplyEffectiveComparableCriteria<TValue>(
        IQueryable<ContentHierarchyItem> query,
        CriteriaSet<PropertyCriterion<TValue>> criteria,
        Expression<Func<ContentHierarchyItem, TValue?>> effective)
            where TValue : struct
    {
        if (criteria.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var criterion in criteria.And)
        {
            query = query.Where(BuildEffectiveComparablePredicate(effective, criterion));
        }

        return query;
    }

    private static Expression<Func<ContentHierarchyItem, bool>> BuildEffectiveComparablePredicate<TValue>(
        Expression<Func<ContentHierarchyItem, TValue?>> effective,
        PropertyCriterion<TValue> criterion)
            where TValue : struct
    {
        var body = effective.Body;
        var constant = Expression.Constant(criterion.Value, typeof(TValue?));
        var nullConstant = Expression.Constant(null, typeof(TValue?));

        var comparison = criterion.Operator switch
        {
            CriteriaValueOperator.Equals => Expression.Equal(body, constant),
            CriteriaValueOperator.NotEquals => Expression.NotEqual(body, constant),
            CriteriaValueOperator.IsNull => Expression.Equal(body, nullConstant),
            CriteriaValueOperator.IsNotNull => Expression.NotEqual(body, nullConstant),
            CriteriaValueOperator.GreaterThan => Expression.GreaterThan(body, constant),
            CriteriaValueOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(body, constant),
            CriteriaValueOperator.LessThan => Expression.LessThan(body, constant),
            CriteriaValueOperator.LessThanOrEqual => Expression.LessThanOrEqual(body, constant),
            var op => throw new NotSupportedException(
                $"Operator {op} is not supported for metadata {typeof(TValue).Name}")
        };

        return Expression.Lambda<Func<ContentHierarchyItem, bool>>(comparison, effective.Parameters);
    }

    private static IQueryable<ContentHierarchyItem> ApplyEffectiveStringCriteria(
        IQueryable<ContentHierarchyItem> query,
        CriteriaSet<PropertyCriterion<string>> criteria,
        Expression<Func<ContentHierarchyItem, string?>> effective)
    {
        if (criteria.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var criterion in criteria.And)
        {
            var value = criterion.Value?.ToLower();
            var predicate = criterion.Operator switch
            {
                CriteriaValueOperator.Equals => Compose(effective, v => v != null && v.ToLower() == value),
                CriteriaValueOperator.NotEquals => Compose(effective, v => v == null || v.ToLower() != value),
                CriteriaValueOperator.IsNull => Compose(effective, v => v == null),
                CriteriaValueOperator.IsNotNull => Compose(effective, v => v != null),
                CriteriaValueOperator.ContainsString =>
                    Compose(effective, v => value != null && v != null && v.ToLower().Contains(value)),
                CriteriaValueOperator.NotContainsString =>
                    Compose(effective, v => value == null || v == null || !v.ToLower().Contains(value)),
                CriteriaValueOperator.StartsWithString =>
                    Compose(effective, v => value != null && v != null && v.ToLower().StartsWith(value)),
                CriteriaValueOperator.NotStartsWithString =>
                    Compose(effective, v => value == null || v == null || !v.ToLower().StartsWith(value)),
                CriteriaValueOperator.EndsWithString =>
                    Compose(effective, v => value != null && v != null && v.ToLower().EndsWith(value)),
                CriteriaValueOperator.NotEndsWithString =>
                    Compose(effective, v => value == null || v == null || !v.ToLower().EndsWith(value)),
                var op => throw new NotSupportedException($"Operator {op} is not supported for metadata string")
            };
            query = query.Where(predicate);
        }

        return query;
    }

    private static Expression<Func<ContentHierarchyItem, bool>> Compose<TValue>(
        Expression<Func<ContentHierarchyItem, TValue>> selector,
        Expression<Func<TValue, bool>> test)
    {
        var replaced = ReplacingExpressionVisitor.Replace(test.Parameters[0], selector.Body, test.Body);
        return Expression.Lambda<Func<ContentHierarchyItem, bool>>(replaced, selector.Parameters);
    }

    private static IQueryable<Relationship> ApplyRelatedContainerIdPredicate(
        IQueryable<Relationship> query,
        PropertyCriterion<Guid> criterion)
    {
        var value = criterion.Value;
        return criterion.Operator switch
        {
            CriteriaValueOperator.Equals => query.Where(r => r.RelatedMetadataContainerId == value),
            CriteriaValueOperator.NotEquals => query.Where(r =>
                r.RelatedMetadataContainerId == null || r.RelatedMetadataContainerId != value),
            CriteriaValueOperator.IsNull => query.Where(r => r.RelatedMetadataContainerId == null),
            CriteriaValueOperator.IsNotNull => query.Where(r => r.RelatedMetadataContainerId != null),
            var op => throw new NotSupportedException($"Operator {op} is not supported for relationship fields")
        };
    }

    private static IQueryable<ContentHierarchyItem> ApplyAcquisitionStatusCriteria(
        IQueryable<ContentHierarchyItem> query,
        CriteriaSet<PropertyCriterion<DownloadStatus>> criteria)
    {
        foreach (var criterion in criteria.And)
        {
            var values = criterion.Values?.ToArray() ?? [];
            query = criterion.Operator switch
            {
                CriteriaValueOperator.Equals when criterion.Value is { } value =>
                    query.Where(i => i.ContentItem != null && i.ContentItem.ActiveAcquisitionStatus == value),
                CriteriaValueOperator.NotEquals when criterion.Value is { } value =>
                    query.Where(i => i.ContentItem == null || i.ContentItem.ActiveAcquisitionStatus != value),
                CriteriaValueOperator.In =>
                    query.Where(i => i.ContentItem != null &&
                        i.ContentItem.ActiveAcquisitionStatus != null &&
                        values.Contains(i.ContentItem.ActiveAcquisitionStatus.Value)),
                CriteriaValueOperator.NotIn =>
                    query.Where(i => i.ContentItem == null ||
                        i.ContentItem.ActiveAcquisitionStatus == null ||
                        !values.Contains(i.ContentItem.ActiveAcquisitionStatus.Value)),
                var op => throw new NotSupportedException($"Operator {op} is not supported for ActiveAcquisitionStatus")
            };
        }

        return query;
    }

    private static IQueryable<ContentHierarchyItem> ApplyMonitoringCriteria(
        IQueryable<ContentHierarchyItem> query,
        CriteriaSet<PropertyCriterion<MonitoringMode>> criteria)
    {
        foreach (var criterion in criteria.And)
        {
            var values = criterion.Values?.ToArray() ?? [];
            query = criterion.Operator switch
            {
                CriteriaValueOperator.Equals when criterion.Value is { } value =>
                    query.Where(i => i.EffectiveMonitoring == value),
                CriteriaValueOperator.NotEquals when criterion.Value is { } value =>
                    query.Where(i => i.EffectiveMonitoring != value),
                CriteriaValueOperator.In => query.Where(i => values.Contains(i.EffectiveMonitoring)),
                CriteriaValueOperator.NotIn => query.Where(i => !values.Contains(i.EffectiveMonitoring)),
                var op => throw new NotSupportedException($"Operator {op} is not supported for Monitoring")
            };
        }

        return query;
    }

    private static IQueryable<ContentHierarchyItem> ApplyQualityStatusCriteria(
        IQueryable<ContentHierarchyItem> query,
        CriteriaSet<PropertyCriterion<QualityStatus>> criteria)
    {
        foreach (var criterion in criteria.And)
        {
            var values = criterion.Values?.ToArray() ?? [];
            query = criterion.Operator switch
            {
                CriteriaValueOperator.Equals when criterion.Value is { } value =>
                    query.Where(i => i.ContentItem != null && i.ContentItem.QualityStatus == value),
                CriteriaValueOperator.NotEquals when criterion.Value is { } value =>
                    query.Where(i => i.ContentItem == null || i.ContentItem.QualityStatus != value),
                CriteriaValueOperator.In =>
                    query.Where(i => i.ContentItem != null &&
                        i.ContentItem.QualityStatus != null &&
                        values.Contains(i.ContentItem.QualityStatus.Value)),
                CriteriaValueOperator.NotIn =>
                    query.Where(i => i.ContentItem == null ||
                        i.ContentItem.QualityStatus == null ||
                        !values.Contains(i.ContentItem.QualityStatus.Value)),
                var op => throw new NotSupportedException($"Operator {op} is not supported for QualityStatus")
            };
        }

        return query;
    }

    private static IQueryable<ContentHierarchyItem> ApplyMediaTotalCriteria<T>(
        IQueryable<ContentHierarchyItem> query,
        CriteriaSet<PropertyCriterion<T>> criteria,
        string property) where T : IEquatable<T>
    {
        foreach (var criterion in criteria.And)
        {
            if (criterion.Operator == CriteriaValueOperator.IsNull)
            {
                query = query.Where(item => item.ContentItem == null || item.ContentItem.MediaReconciledAt == null);
                continue;
            }

            query = query.Where(item => item.ContentItem != null && item.ContentItem.MediaReconciledAt != null);
            if (criterion.Operator == CriteriaValueOperator.IsNotNull)
            {
                continue;
            }

            var matching = query.Select(item => item.ContentItem!);
            var parameter = Expression.Parameter(typeof(ContentItem), "item");
            var member = Expression.Property(parameter, property);
            var value = Expression.Constant(criterion.Value, typeof(T));
            var comparison = criterion.Operator switch
            {
                CriteriaValueOperator.GreaterThan => Expression.GreaterThan(member, value),
                CriteriaValueOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(member, value),
                CriteriaValueOperator.LessThan => Expression.LessThan(member, value),
                CriteriaValueOperator.LessThanOrEqual => Expression.LessThanOrEqual(member, value),
                _ => null
            };
            matching = comparison is null
                ? matching.ApplyCriteriaSet(new CriteriaSet<PropertyCriterion<T>> { And = [criterion] }, property)
                : matching.Where(Expression.Lambda<Func<ContentItem, bool>>(comparison, parameter));
            var matchingIds = matching.Select(item => item.ContentHierarchyItemId);
            query = query.Where(item => matchingIds.Contains(item.Id));
        }

        return query;
    }

    private static IQueryable<ContentHierarchyItem> ApplyFulfillmentCriteria(
        IQueryable<ContentHierarchyItem> query,
        CriteriaSet<PropertyCriterion<FulfillmentStatus>> criteria)
    {
        if (!criteria.And.Any())
        {
            return query;
        }

        foreach (var criterion in criteria.And)
        {
            query = ApplyFulfillmentCriterion(query, criterion);
        }

        return query;
    }

    private static IQueryable<ContentHierarchyItem> ApplyFulfillmentCriterion(
        IQueryable<ContentHierarchyItem> query,
        PropertyCriterion<FulfillmentStatus> criterion)
    {
        return criterion.Operator switch
        {
            CriteriaValueOperator.Equals when criterion.Value is { } value =>
                value == FulfillmentStatus.Missing
                    ? query.Where(i => i.ContentItem == null || i.ContentItem.Fulfillment == value)
                    : query.Where(i => i.ContentItem != null && i.ContentItem.Fulfillment == value),
            CriteriaValueOperator.NotEquals when criterion.Value is { } value =>
                value == FulfillmentStatus.Missing
                    ? query.Where(i => i.ContentItem != null && i.ContentItem.Fulfillment != value)
                    : query.Where(i => i.ContentItem == null || i.ContentItem.Fulfillment != value),
            CriteriaValueOperator.In => ApplyFulfillmentInCriterion(query, criterion),
            CriteriaValueOperator.NotIn => ApplyFulfillmentNotInCriterion(query, criterion),
            var op => throw new NotSupportedException($"Operator {op} is not supported for Fulfillment")
        };
    }

    private static IQueryable<ContentHierarchyItem> ApplyFulfillmentInCriterion(
        IQueryable<ContentHierarchyItem> query,
        PropertyCriterion<FulfillmentStatus> criterion)
    {
        var values = criterion.Values?.ToArray() ?? [];
        return values.Contains(FulfillmentStatus.Missing)
            ? query.Where(i => i.ContentItem == null || values.Contains(i.ContentItem.Fulfillment))
            : query.Where(i => i.ContentItem != null && values.Contains(i.ContentItem.Fulfillment));
    }

    private static IQueryable<ContentHierarchyItem> ApplyFulfillmentNotInCriterion(
        IQueryable<ContentHierarchyItem> query,
        PropertyCriterion<FulfillmentStatus> criterion)
    {
        var values = criterion.Values?.ToArray() ?? [];
        return values.Contains(FulfillmentStatus.Missing)
            ? query.Where(i => i.ContentItem != null && !values.Contains(i.ContentItem.Fulfillment))
            : query.Where(i => i.ContentItem == null || !values.Contains(i.ContentItem.Fulfillment));
    }

    private static IQueryable<ContentHierarchyItem> ApplySort(
        IModel model,
        IQueryable<ContentHierarchyItem> query,
        SortParameter? sort)
    {
        if (sort is null)
        {
            return query.OrderBy(i => i.Name).ThenBy(i => i.Id);
        }

        var sortProperty = ResolveSortProperty<ContentHierarchyItem>(model, sort.PropertyName);
        if (sortProperty is not null)
        {
            return sort.Direction == SortDirection.Ascending
                ? query.OrderBy(i => EF.Property<object>(i, sortProperty)).ThenBy(i => i.Id)
                : query.OrderByDescending(i => EF.Property<object>(i, sortProperty)).ThenBy(i => i.Id);
        }

        var contentItemProperty = ResolveSortProperty<ContentItem>(model, sort.PropertyName);
        if (contentItemProperty is not null)
        {
            return ApplyContentItemSort(query, contentItemProperty, sort.Direction);
        }

        var metadataProperty = MetadataSortProperties
            .FirstOrDefault(name => string.Equals(name, sort.PropertyName, StringComparison.OrdinalIgnoreCase));
        if (metadataProperty is not null)
        {
            return ApplyMetadataSort(query, metadataProperty, sort.Direction);
        }

        return query.OrderBy(i => i.Name).ThenBy(i => i.Id);
    }

    private static string? ResolveSortProperty<TEntity>(IModel model, string? propertyName)
    {
        return model.FindEntityType(typeof(TEntity))!.GetProperties()
            .Where(property => !property.IsShadowProperty() &&
                (property.ClrType.IsValueType || property.ClrType == typeof(string) ||
                    property.ClrType == typeof(Uri)))
            .Select(property => property.Name)
            .FirstOrDefault(name => string.Equals(name, propertyName, StringComparison.OrdinalIgnoreCase));
    }

    private static IQueryable<ContentHierarchyItem> ApplyContentItemSort(
        IQueryable<ContentHierarchyItem> query,
        string contentItemProperty,
        SortDirection direction)
    {
        return contentItemProperty switch
        {
            nameof(ContentItem.DateLastSearched) => OrderByDateLastSearched(query, direction),
            nameof(ContentItem.FulfilledAt) => Order(
                query.OrderBy(i => i.ContentItem == null || i.ContentItem.FulfilledAt == null),
                i => i.ContentItem == null ? null : i.ContentItem.FulfilledAt,
                direction),
            nameof(ContentItem.PrimaryMediaSize) => Order(
                query.OrderBy(i => i.ContentItem == null || i.ContentItem.MediaReconciledAt == null),
                i => i.ContentItem == null || i.ContentItem.MediaReconciledAt == null
                    ? (long?)null : i.ContentItem.PrimaryMediaSize,
                direction),
            nameof(ContentItem.PrimaryMediaFileCount) => Order(
                query.OrderBy(i => i.ContentItem == null || i.ContentItem.MediaReconciledAt == null),
                i => i.ContentItem == null || i.ContentItem.MediaReconciledAt == null
                    ? (int?)null : i.ContentItem.PrimaryMediaFileCount,
                direction),
            _ => query.OrderBy(i => i.Name).ThenBy(i => i.Id),
        };
    }

    private static IQueryable<ContentHierarchyItem> OrderByDateLastSearched(
        IQueryable<ContentHierarchyItem> query,
        SortDirection direction)
    {
        return direction == SortDirection.Ascending
            ? query
                .OrderBy(i => i.ContentItem == null || i.ContentItem.DateLastSearched == null)
                .ThenBy(i => i.ContentItem == null ? null : i.ContentItem.DateLastSearched)
                .ThenBy(i => i.Name)
                .ThenBy(i => i.Id)
            : query
                .OrderBy(i => i.ContentItem == null || i.ContentItem.DateLastSearched == null)
                .ThenByDescending(i => i.ContentItem == null ? null : i.ContentItem.DateLastSearched)
                .ThenBy(i => i.Name)
                .ThenBy(i => i.Id);
    }

    private static IQueryable<ContentHierarchyItem> ApplyMetadataSort(
        IQueryable<ContentHierarchyItem> query,
        string metadataProperty,
        SortDirection direction)
    {
        return metadataProperty switch
        {
            nameof(MaterializedMetadata.PublishDate) => Order(
                query,
                i => i.MetadataContainer == null
                    ? null
                    : i.MetadataContainer.Contributions
                        .Where(c => c.PublishDate != null)
                        .OrderBy(c => c.Ordinal)
                        .Select(c => c.PublishDate)
                        .FirstOrDefault(),
                direction),
            nameof(MaterializedMetadata.Duration) => Order(
                query,
                i => i.MetadataContainer == null
                    ? null
                    : i.MetadataContainer.Contributions
                        .Where(c => c.Duration != null)
                        .OrderBy(c => c.Ordinal)
                        .Select(c => c.Duration)
                        .FirstOrDefault(),
                direction),
            nameof(MaterializedMetadata.SequenceNumber) => OrderBySequenceNumber(query, direction),
            nameof(MaterializedMetadata.SortTitle) => Order(
                query,
                i => i.MetadataContainer == null
                    ? null
                    : i.MetadataContainer.Contributions
                        .Where(c => c.SortTitle != null)
                        .OrderBy(c => c.Ordinal)
                        .Select(c => c.SortTitle)
                        .FirstOrDefault(),
                direction),
            _ => query.OrderBy(i => i.Name).ThenBy(i => i.Id),
        };
    }

    private static IQueryable<ContentHierarchyItem> Order<TKey>(
        IQueryable<ContentHierarchyItem> query,
        Expression<Func<ContentHierarchyItem, TKey>> keySelector,
        SortDirection direction)
    {
        return direction == SortDirection.Ascending
            ? query.OrderBy(keySelector).ThenBy(i => i.Id)
            : query.OrderByDescending(keySelector).ThenBy(i => i.Id);
    }

    private static IQueryable<ContentHierarchyItem> Order<TKey>(
        IOrderedQueryable<ContentHierarchyItem> query,
        Expression<Func<ContentHierarchyItem, TKey>> keySelector,
        SortDirection direction)
    {
        return direction == SortDirection.Ascending
            ? query.ThenBy(keySelector).ThenBy(i => i.Id)
            : query.ThenByDescending(keySelector).ThenBy(i => i.Id);
    }

    private static IQueryable<ContentHierarchyItem> OrderBySequenceNumber(
        IQueryable<ContentHierarchyItem> query,
        SortDirection direction)
    {
        Expression<Func<ContentHierarchyItem, int?>> keySelector = i => i.MetadataContainer == null
            ? null
            : i.MetadataContainer.Contributions
                .Where(c => c.SequenceNumber != null)
                .OrderBy(c => c.Ordinal)
                .Select(c => c.SequenceNumber)
                .FirstOrDefault();

        var ranked = query.OrderBy(i => (i.MetadataContainer == null
            ? null
            : i.MetadataContainer.Contributions
                .Where(c => c.SequenceNumber != null)
                .OrderBy(c => c.Ordinal)
                .Select(c => c.SequenceNumber)
                .FirstOrDefault()) == null ? 1 : 0);
        var ordered = direction == SortDirection.Ascending
            ? ranked.ThenBy(keySelector)
            : ranked.ThenByDescending(keySelector);
        return ordered.ThenBy(i => i.Slug).ThenBy(i => i.Id);
    }

    private static EffectiveValues ResolveEffectiveValues(
        EffectiveNode node,
        IReadOnlyDictionary<Guid, EffectiveNode> nodesById,
        IReadOnlyList<RootFolder> rootFolders,
        bool globalMonitoring,
        IContentTypeRegistry contentTypeRegistry)
    {
        MonitoringMode? monitoring = null;
        Guid? rootFolderId = null;
        Guid? qualityTierListId = null;
        var current = (EffectiveNode?)node;
        var visited = new HashSet<Guid>();
        while (current is { } currentNode && visited.Add(currentNode.Id))
        {
            monitoring ??= currentNode.Monitoring;
            rootFolderId ??= currentNode.RootFolderId;
            if (qualityTierListId is null &&
                currentNode.QualityTierListId is { } explicitQualityTierListId &&
                explicitQualityTierListId != Guid.Empty)
            {
                qualityTierListId = explicitQualityTierListId;
            }

            current = currentNode.ParentId is { } parentId && nodesById.TryGetValue(parentId, out var parent)
                ? parent
                : null;
        }

        rootFolderId ??= ResolveExistingRootFolderId(node.EffectiveRootFolderId, node.ContentType, rootFolders);
        rootFolderId ??= ResolveDefaultRootFolderId(rootFolders, node.ContentType);
        var rootFolder = rootFolderId is { } resolvedRootFolderId
            ? rootFolders.FirstOrDefault(folder => folder.Id == resolvedRootFolderId)
            : null;
        if (monitoring is null && rootFolder?.DefaultMonitoring is { } defaultMonitoring)
        {
            monitoring = ToMonitoringMode(defaultMonitoring);
        }

        var usesGlobalMonitoring = monitoring is null;
        monitoring ??= ToMonitoringMode(globalMonitoring);
        var usesLevelDefaultQualityTierList = qualityTierListId is null;
        if (usesLevelDefaultQualityTierList &&
            rootFolder is not null &&
            string.Equals(rootFolder.ContentType, node.ContentType, StringComparison.Ordinal))
        {
            var levelsBySlug = contentTypeRegistry.GetDefinitionFor(node.ContentType)?.LevelDefinitions
                .ToDictionary(level => level.Slug, StringComparer.Ordinal);
            var currentLevelSlug = node.Level;
            var visitedLevelSlugs = new HashSet<string>(StringComparer.Ordinal);
            while (visitedLevelSlugs.Add(currentLevelSlug))
            {
                if (rootFolder.Defaults.TryGetValue(currentLevelSlug, out var configured))
                {
                    qualityTierListId = configured.QualityTierListId;
                    break;
                }

                if (levelsBySlug is null ||
                    !levelsBySlug.TryGetValue(currentLevelSlug, out var level) ||
                    level.ParentSlug is not { } parentSlug)
                {
                    break;
                }

                currentLevelSlug = parentSlug;
            }
        }

        return new EffectiveValues(
            monitoring.Value,
            rootFolderId,
            qualityTierListId,
            usesGlobalMonitoring,
            usesLevelDefaultQualityTierList);
    }

    private static Guid? ResolveExistingRootFolderId(
        Guid effectiveRootFolderId,
        string contentType,
        IReadOnlyList<RootFolder> rootFolders)
    {
        return rootFolders.Any(folder => folder.Id == effectiveRootFolderId &&
            string.Equals(folder.ContentType, contentType, StringComparison.Ordinal))
            ? effectiveRootFolderId
            : null;
    }

    private static Guid? ResolveDefaultRootFolderId(IReadOnlyList<RootFolder> rootFolders, string contentType)
    {
        var matching = rootFolders
            .Where(folder => string.Equals(folder.ContentType, contentType, StringComparison.Ordinal))
            .ToList();
        if (matching.Count == 1)
        {
            return matching[0].Id;
        }

        var defaults = matching.Where(folder => folder.IsDefault).ToList();
        return defaults.Count == 1 ? defaults[0].Id : null;
    }

    private static void MergeChanges(
        IDictionary<Guid, HashSet<string>> destination,
        Guid id,
        IEnumerable<string> fields)
    {
        if (!destination.TryGetValue(id, out var existing))
        {
            existing = [];
            destination[id] = existing;
        }

        existing.UnionWith(fields);
    }

    private static void MergeChanges(
        IDictionary<Guid, HashSet<string>> destination,
        IReadOnlyDictionary<Guid, IReadOnlyCollection<string>> source)
    {
        foreach (var (id, fields) in source)
        {
            MergeChanges(destination, id, fields);
        }
    }

    private static IReadOnlyCollection<string> ChangedFields(ContentHierarchyItemPatch patch)
    {
        var fields = new List<string>();
        if (patch.Monitoring is not null)
        {
            fields.Add(nameof(ContentHierarchyItem.Monitoring));
        }

        if (patch.RootFolderId is not null)
        {
            fields.Add(nameof(ContentHierarchyItem.RootFolderId));
        }

        if (patch.QualityTierListId is not null)
        {
            fields.Add(nameof(ContentHierarchyItem.QualityTierListId));
        }

        if (patch.Path is not null)
        {
            fields.Add(nameof(ContentHierarchyItem.Path));
        }

        if (patch.LanguagePreferences is not null)
        {
            fields.Add(nameof(ContentHierarchyItem.LanguagePreferences));
        }

        if (patch.PendingMetadataRefresh is not null)
        {
            fields.Add(nameof(ContentHierarchyItem.PendingMetadataRefresh));
        }

        if (patch.Tags is not null)
        {
            fields.Add("Tags");
        }

        if (patch.Name is not null)
        {
            fields.Add(nameof(ContentHierarchyItem.Name));
        }

        if (patch.Slug is not null)
        {
            fields.Add(nameof(ContentHierarchyItem.Slug));
        }

        if (patch.Settings is not null)
        {
            fields.Add(nameof(ContentHierarchyItem.Settings));
        }

        if (patch.DateLastRefreshed is not null)
        {
            fields.Add(nameof(ContentHierarchyItem.DateLastRefreshed));
        }

        return fields;
    }

    private static IReadOnlyCollection<string> ChangedFields(ContentHierarchyItem current, ContentHierarchyItem model)
    {
        var fields = new List<string>();
        if (current.ParentId != model.ParentId)
        {
            fields.Add(nameof(ContentHierarchyItem.ParentId));
        }

        if (current.QualityTierListId != model.QualityTierListId)
        {
            fields.Add(nameof(ContentHierarchyItem.QualityTierListId));
        }

        if (current.RootFolderId != model.RootFolderId)
        {
            fields.Add(nameof(ContentHierarchyItem.RootFolderId));
        }

        if (!string.Equals(current.ContentType, model.ContentType, StringComparison.Ordinal))
        {
            fields.Add(nameof(ContentHierarchyItem.ContentType));
        }

        if (!string.Equals(current.Level, model.Level, StringComparison.Ordinal))
        {
            fields.Add(nameof(ContentHierarchyItem.Level));
        }

        if (!string.Equals(current.Slug, model.Slug, StringComparison.Ordinal))
        {
            fields.Add(nameof(ContentHierarchyItem.Slug));
        }

        if (!string.Equals(current.Name, model.Name, StringComparison.Ordinal))
        {
            fields.Add(nameof(ContentHierarchyItem.Name));
        }

        if (current.Monitoring != model.Monitoring)
        {
            fields.Add(nameof(ContentHierarchyItem.Monitoring));
        }

        if (!string.Equals(current.Path, model.Path, StringComparison.Ordinal))
        {
            fields.Add(nameof(ContentHierarchyItem.Path));
        }

        if (!current.LanguagePreferences.SequenceEqual(model.LanguagePreferences))
        {
            fields.Add(nameof(ContentHierarchyItem.LanguagePreferences));
        }

        if (!current.Settings.OrderBy(entry => entry.Key).SequenceEqual(model.Settings.OrderBy(entry => entry.Key)))
        {
            fields.Add(nameof(ContentHierarchyItem.Settings));
        }

        if (current.DateLastRefreshed != model.DateLastRefreshed)
        {
            fields.Add(nameof(ContentHierarchyItem.DateLastRefreshed));
        }

        if (current.PendingMetadataRefresh != model.PendingMetadataRefresh)
        {
            fields.Add(nameof(ContentHierarchyItem.PendingMetadataRefresh));
        }

        return fields;
    }

    private static IReadOnlyList<Guid> AncestorIdsOf(Guid? parentId, IReadOnlyDictionary<Guid, EffectiveNode> nodes)
    {
        var ancestors = new List<Guid>();
        var visited = new HashSet<Guid>();
        var current = parentId;
        while (current is { } ancestorId && visited.Add(ancestorId))
        {
            ancestors.Add(ancestorId);
            current = nodes.TryGetValue(ancestorId, out var ancestor) ? ancestor.ParentId : null;
        }

        return ancestors;
    }

    private static ContentEventImpact ImpactOf(
        Guid id,
        IReadOnlyDictionary<Guid, EffectiveNode> nodes,
        ContentEventImpact? current = null,
        ContentEventImpact? previous = null)
    {
        if (!nodes.TryGetValue(id, out var node))
        {
            var known = (current ?? new()) with { HierarchyItemId = id };
            return known with
            {
                TopLevel = known.ParentId is null,
                Previous = previous is null ? null : previous with { TopLevel = previous.ParentId is null }
            };
        }

        var ancestors = AncestorIdsOf(node.ParentId, nodes);
        var impact = new ContentEventImpact
        {
            HierarchyItemId = id,
            MetadataContainerId = node.MetadataContainerId,
            ParentId = node.ParentId,
            AncestorIds = ancestors,
            RootId = ancestors.Count > 0 ? ancestors[^1] : id,
            ContentType = node.ContentType,
            Level = node.Level,
            TopLevel = node.ParentId is null,
            RootFolderId = node.RootFolderId,
            QualityTierListId = node.QualityTierListId,
            Monitoring = node.EffectiveMonitoring != MonitoringMode.Unmonitored
        };

        if (previous is null)
        {
            return impact;
        }

        var previousAncestors = previous.ParentId == node.ParentId
            ? impact.AncestorIds
            : AncestorIdsOf(previous.ParentId, nodes);
        return impact with
        {
            Previous = previous with
            {
                AncestorIds = previousAncestors,
                RootId = previousAncestors.Count > 0 ? previousAncestors[^1] : id,
                TopLevel = previous.ParentId is null
            }
        };
    }

    private static MonitoringMode ToMonitoringMode(bool monitored) =>
        monitored ? MonitoringMode.Monitored : MonitoringMode.Unmonitored;

    private async Task<bool> ResolveGlobalMonitoringAsync(CancellationToken cancellationToken)
    {
        const string groupName = "monitoring";
        const string key = "system-default";
        var raw = await (
            from settingsGroup in context.SettingsGroups.AsNoTracking()
            join setting in context.Settings.AsNoTracking() on settingsGroup.Id equals setting.SettingsGroupId
            where settingsGroup.ExtensionId == null && settingsGroup.Name == groupName && setting.Key == key
            select setting.Value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (raw is null or "1")
        {
            return true;
        }

        if (raw == "0")
        {
            return false;
        }

        if (bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        logger.LogWarning(
            "The hierarchy item repository cannot read the global monitoring default {MonitoringDefault} and " +
            "monitors the library by default.",
            raw);
        return true;
    }

    private async Task<IReadOnlyList<EffectiveNode>> ListEffectiveNodesAsync(
        EffectiveValueTrigger trigger,
        CancellationToken cancellationToken)
    {
        Guid?[] subtreeRoots = trigger.Kind switch
        {
            EffectiveValueTriggerKind.ItemSubtree => [trigger.ItemId],
            EffectiveValueTriggerKind.Reparent => [trigger.ItemId, trigger.OldParentId, trigger.NewParentId],
            _ => []
        };

        if (subtreeRoots.Length == 0)
        {
            return await ToEffectiveNodes(context.ContentHierarchyItems.AsNoTracking())
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var nodesById = new Dictionary<Guid, EffectiveNode>();
        var rows = await ListNodesByIdAsync(
            [.. subtreeRoots.OfType<Guid>().Distinct()],
            cancellationToken).ConfigureAwait(false);
        while (rows.Count > 0)
        {
            var added = new List<Guid>();
            foreach (var row in rows)
            {
                if (nodesById.TryAdd(row.Id, row))
                {
                    added.Add(row.Id);
                }
            }

            rows = await ListChildNodesAsync(added, cancellationToken).ConfigureAwait(false);
        }

        await AddAncestorNodesAsync(nodesById, cancellationToken).ConfigureAwait(false);
        return [.. nodesById.Values];
    }

    private async Task<List<EffectiveNode>> ListNodesByIdAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var rows = new List<EffectiveNode>();
        foreach (var batch in ids.Chunk(AncestryBatchSize))
        {
            rows.AddRange(await ToEffectiveNodes(
                context.ContentHierarchyItems.AsNoTracking().Where(item => batch.Contains(item.Id)))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false));
        }

        return rows;
    }

    private async Task<List<EffectiveNode>> ListChildNodesAsync(
        IReadOnlyList<Guid> parentIds,
        CancellationToken cancellationToken)
    {
        var rows = new List<EffectiveNode>();
        foreach (var batch in parentIds.Chunk(AncestryBatchSize))
        {
            rows.AddRange(await ToEffectiveNodes(
                context.ContentHierarchyItems.AsNoTracking()
                    .Where(item => item.ParentId != null && batch.Contains(item.ParentId.Value)))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false));
        }

        return rows;
    }

    private async Task AddAncestorNodesAsync(
        Dictionary<Guid, EffectiveNode> nodesById,
        CancellationToken cancellationToken)
    {
        List<Guid> MissingParentIds(IEnumerable<EffectiveNode> nodes) =>
        [
            .. nodes
                .Select(node => node.ParentId)
                .OfType<Guid>()
                .Where(id => !nodesById.ContainsKey(id))
                .Distinct()
        ];

        var pending = MissingParentIds(nodesById.Values);
        while (pending.Count > 0)
        {
            var rows = await ListNodesByIdAsync(pending, cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                nodesById.TryAdd(row.Id, row);
            }

            pending = MissingParentIds(rows);
        }
    }

    private async Task<Dictionary<Guid, EffectiveNode>> PrepareEffectiveValuesForCreateAsync(
        ContentHierarchyItem model,
        CancellationToken cancellationToken)
    {
        var rootFolders = await context.RootFolders
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var defaultRootFolderId = ResolveDefaultRootFolderId(rootFolders, model.ContentType);
        if (defaultRootFolderId is null)
        {
            throw new MagpieException($"Defaults could not be determined for content type {model.ContentType}.");
        }

        var nodesById = new Dictionary<Guid, EffectiveNode>();
        var ownNode = new EffectiveNode(
            model.Id,
            model.ParentId,
            model.ContentType,
            model.Level,
            model.MetadataContainerId,
            model.Monitoring,
            model.RootFolderId,
            model.QualityTierListId,
            model.EffectiveMonitoring,
            model.EffectiveRootFolderId,
            model.EffectiveQualityTierListId);
        nodesById[ownNode.Id] = ownNode;
        await AddAncestorNodesAsync(nodesById, cancellationToken).ConfigureAwait(false);
        var globalMonitoring = await ResolveGlobalMonitoringAsync(cancellationToken).ConfigureAwait(false);
        var values = ResolveEffectiveValues(
            ownNode,
            nodesById,
            rootFolders,
            globalMonitoring,
            contentTypeRegistry);
        if (values.RootFolderId is null)
        {
            throw new MagpieException($"Defaults could not be determined for content type {model.ContentType}.");
        }

        model.EffectiveMonitoring = values.Monitoring;
        model.EffectiveRootFolderId = values.RootFolderId.Value;
        model.EffectiveQualityTierListId = values.QualityTierListId;
        nodesById[model.Id] = ownNode with
        {
            EffectiveMonitoring = model.EffectiveMonitoring,
            EffectiveRootFolderId = model.EffectiveRootFolderId,
            EffectiveQualityTierListId = model.EffectiveQualityTierListId
        };

        return nodesById;
    }

    private async Task PublishEffectiveChangesAsync(EffectiveRecompute recompute, CancellationToken cancellationToken)
    {
        foreach (var (id, fields) in recompute.Changes)
        {
            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    ContentEventBuilder.BuildContentHierarchyItemUpdatedEvent(ImpactOf(id, recompute.Nodes), fields),
                    token),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PublishChangesAsync(
        IReadOnlyDictionary<Guid, HashSet<string>> changes,
        IReadOnlyDictionary<Guid, EffectiveNode> nodes,
        IReadOnlyDictionary<Guid, ScopeChange> scopes,
        CancellationToken cancellationToken)
    {
        foreach (var (id, fields) in changes)
        {
            scopes.TryGetValue(id, out var scope);
            var impact = ImpactOf(id, nodes, scope.Current, scope.Previous);
            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    ContentEventBuilder.BuildContentHierarchyItemUpdatedEvent(impact, fields),
                    token),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<PatchOutcome> ApplyPatchAsync(
        Guid id,
        ContentHierarchyItemPatch patch,
        Guid? modifiedByUserId,
        DateTime now,
        IDictionary<Guid, HashSet<string>> publishedChanges,
        IDictionary<Guid, EffectiveNode> nodes,
        CancellationToken cancellationToken)
    {
        var model = await context.ContentHierarchyItems
            .Include(i => i.MetadataContainer)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (model is null)
        {
            return new(false, null, null);
        }

        if (model.MetadataContainer is null)
        {
            logger.LogError(
                "The hierarchy item repository cannot patch item {ContentHierarchyItemId} because its metadata " +
                "container {MetadataContainerId} is missing.",
                id,
                model.MetadataContainerId);
            return new(false, null, null);
        }

        var previous = new ContentEventImpact
        {
            MetadataContainerId = model.MetadataContainerId,
            ParentId = model.ParentId,
            ContentType = model.ContentType,
            Level = model.Level,
            TopLevel = model.ParentId is null,
            RootFolderId = model.RootFolderId,
            QualityTierListId = model.QualityTierListId,
            Monitoring = model.EffectiveMonitoring != MonitoringMode.Unmonitored
        };

        if (patch.QualityTierListId is not null)
        {
            model.QualityTierListId = patch.QualityTierListId.Value;
        }

        if (patch.RootFolderId is not null)
        {
            model.RootFolderId = patch.RootFolderId.Value;
        }

        if (patch.Monitoring is not null)
        {
            model.Monitoring = patch.Monitoring.Value;
        }

        if (patch.Path is not null)
        {
            model.Path = patch.Path.Value;
            model.Settings.Remove("content.path.origin");
        }

        if (patch.LanguagePreferences is not null)
        {
            var languageList = model.LanguagePreferences.ToList();

            if (patch.LanguagePreferences.Remove is not null)
            {
                var indexesToRemove = patch.LanguagePreferences.Remove.OrderByDescending(i => i).ToList();
                foreach (var index in indexesToRemove)
                {
                    if (index >= 0 && index < languageList.Count)
                    {
                        languageList.RemoveAt(index);
                    }
                }
            }

            if (patch.LanguagePreferences.Update is not null)
            {
                foreach (var (index, value) in patch.LanguagePreferences.Update)
                {
                    if (index >= 0 && index < languageList.Count)
                    {
                        languageList[index] = value;
                    }
                }
            }

            if (patch.LanguagePreferences.Insert is not null)
            {
                foreach (var (index, value) in patch.LanguagePreferences.Insert.OrderBy(kvp => kvp.Key))
                {
                    if (index >= 0 && index <= languageList.Count)
                    {
                        languageList.Insert(index, value);
                    }
                }
            }

            model.LanguagePreferences = languageList;
        }

        if (patch.PendingMetadataRefresh is not null)
        {
            model.PendingMetadataRefresh = patch.PendingMetadataRefresh.Value;
        }

        if (patch.Name is not null)
        {
            model.Name = patch.Name.Value;
        }

        if (patch.Slug is not null)
        {
            model.Slug = patch.Slug.Value;
        }

        if (patch.DateLastRefreshed is not null)
        {
            model.DateLastRefreshed = patch.DateLastRefreshed.Value;
        }

        if (patch.Settings is not null)
        {
            ApplyPatch(model.Settings, patch.Settings);
            context.Entry(model).Property(i => i.Settings).IsModified = true;
        }

        var tagsAdded = new List<string>();
        var tagsRemoved = new List<string>();

        if (patch.Tags is not null)
        {
            if (patch.Tags.Add is not null)
            {
                foreach (var tagName in patch.Tags.Add)
                {
                    var tag = await context.Tags
                        .Include(t => t.MetadataContainers)
                        .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                        .ConfigureAwait(false);

                    if (tag is null)
                    {
                        tag = new() { Name = tagName };
                        context.Tags.Add(tag);
                    }

                    if (!tag.MetadataContainers.Contains(model.MetadataContainer))
                    {
                        tag.MetadataContainers.Add(model.MetadataContainer);
                        tagsAdded.Add(tagName);
                    }
                }
            }

            if (patch.Tags.Remove is not null)
            {
                foreach (var tagName in patch.Tags.Remove)
                {
                    var tag = await context.Tags
                        .Include(t => t.MetadataContainers)
                        .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                        .ConfigureAwait(false);

                    if (tag is not null && tag.MetadataContainers.Remove(model.MetadataContainer))
                    {
                        tagsRemoved.Add(tagName);
                    }
                }
            }
        }

        model.ModifiedAt = now;
        model.ModifiedByUserId = modifiedByUserId;

        var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (updated > 0)
        {
            await auditRecordRepository.CreateAsync(
                new()
                {
                    Category = AuditCategories.Content,
                    Operation = "Patch",
                    EntityId = id.ToString(),
                    EntityType = nameof(ContentHierarchyItem),
                    Detail = $"Updated content item {model.Name}.",
                    Timestamp = now,
                    UserId = modifiedByUserId
                },
                cancellationToken).ConfigureAwait(false);

            foreach (var tagName in tagsAdded)
            {
                await auditRecordRepository.CreateAsync(
                    new()
                    {
                        Category = AuditCategories.Content,
                        Operation = "AddTag",
                        EntityId = id.ToString(),
                        EntityType = nameof(ContentHierarchyItem),
                        Detail = $"Added tag {tagName} to content item {model.Name}.",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (var tagName in tagsRemoved)
            {
                await auditRecordRepository.CreateAsync(
                    new()
                    {
                        Category = AuditCategories.Content,
                        Operation = "RemoveTag",
                        EntityId = id.ToString(),
                        EntityType = nameof(ContentHierarchyItem),
                        Detail = $"Removed tag {tagName} from content item {model.Name}.",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            if (patch.Monitoring is not null || patch.RootFolderId is not null || patch.QualityTierListId is not null)
            {
                var recompute = await RecomputeEffectiveValuesAsync(
                    new EffectiveValueTrigger(EffectiveValueTriggerKind.ItemSubtree, ItemId: id),
                    cancellationToken).ConfigureAwait(false);
                MergeChanges(publishedChanges, recompute.Changes);
                foreach (var (nodeId, node) in recompute.Nodes)
                {
                    nodes[nodeId] = node;
                }
            }
        }

        return new(
            updated > 0,
            new()
            {
                MetadataContainerId = model.MetadataContainerId,
                ParentId = model.ParentId,
                ContentType = model.ContentType,
                Level = model.Level,
                TopLevel = model.ParentId is null,
                RootFolderId = model.RootFolderId,
                QualityTierListId = model.QualityTierListId,
                Monitoring = model.EffectiveMonitoring != MonitoringMode.Unmonitored
            },
            previous);
    }

    private IQueryable<ContentHierarchyItem> ApplyCriteria(
        IQueryable<ContentHierarchyItem> query,
        ContentHierarchyItemCriteria criteria)
    {
        query = query.ApplyCriteriaSet(criteria.ParentId, nameof(ContentHierarchyItem.ParentId));
        if (criteria.RootLevelOnly == true)
        {
            query = query.Where(i => i.ParentId == null);
        }

        if (criteria.LeafOnly == true)
        {
            query = query.Where(i => !context.ContentHierarchyItems.Any(c => c.ParentId == i.Id));
        }

        query = query.ApplyCriteriaSet(criteria.RootFolderId, nameof(ContentHierarchyItem.RootFolderId));
        if (criteria.ContentItemId is { } contentItemId)
        {
            query = query.Where(i => i.ContentItem != null && i.ContentItem.Id == contentItemId);
        }

        query = query.ApplyNormalizedCriteriaSet(criteria.ContentType, nameof(ContentHierarchyItem.ContentType));
        query = query.ApplyNormalizedCriteriaSet(criteria.Level, nameof(ContentHierarchyItem.Level));
        query = query.ApplyNormalizedCriteriaSet(criteria.Slug, nameof(ContentHierarchyItem.Slug));
        query = query.ApplyCriteriaSet(criteria.Name, nameof(ContentHierarchyItem.Name));
        query = ApplyMonitoringCriteria(query, criteria.Monitoring);
        if (criteria.EffectiveMonitoring is { } effectiveMonitoring)
        {
            query = query.Where(item =>
                (item.EffectiveMonitoring != MonitoringMode.Unmonitored) == effectiveMonitoring);
        }

        if (criteria.HasMonitoredParent is { } hasMonitoredParent)
        {
            query = query.Where(i => context.ContentHierarchyItems.Any(p =>
                p.Id == i.ParentId && p.EffectiveMonitoring != MonitoringMode.Unmonitored) == hasMonitoredParent);
        }

        if (criteria.EffectiveRootFolderId is { } effectiveRootFolderId)
        {
            query = query.Where(item => item.EffectiveRootFolderId == effectiveRootFolderId);
        }

        query = ApplyFulfillmentCriteria(query, criteria.Fulfillment);
        query = ApplyMediaTotalCriteria(query, criteria.PrimaryMediaSize, nameof(ContentItem.PrimaryMediaSize));
        query = ApplyMediaTotalCriteria(query, criteria.PrimaryMediaFileCount,
            nameof(ContentItem.PrimaryMediaFileCount));
        query = ApplyAcquisitionStatusCriteria(query, criteria.ActiveAcquisitionStatus);
        query = ApplyQualityStatusCriteria(query, criteria.QualityStatus);
        query = ApplyMediaFileCriteria(query, criteria.MediaFiles);
        if (criteria.PendingMetadataRefresh is { } pendingMetadataRefresh)
        {
            query = query.Where(i => i.PendingMetadataRefresh == pendingMetadataRefresh);
        }

        if (criteria.ImportPending is { } importPending)
        {
            var pendingIds = ImportPendingItemIds();
            query = importPending
                ? query.Where(i => pendingIds.Contains(i.Id))
                : query.Where(i => !pendingIds.Contains(i.Id));
        }

        if (criteria.LastRefreshedBefore is { } lastRefreshedBefore)
        {
            query = query.Where(i =>
                i.DateLastRefreshed == null || i.DateLastRefreshed < lastRefreshedBefore);
        }

        if (criteria.HasMetadataExternalIds is { } hasMetadataExternalIds)
        {
            query = hasMetadataExternalIds
                ? query.Where(i => context.MetadataContributions
                    .Any(c => c.MetadataContainerId == i.MetadataContainerId && c.ExternalIds.Any()))
                : query.Where(i => !context.MetadataContributions
                    .Any(c => c.MetadataContainerId == i.MetadataContainerId && c.ExternalIds.Any()));
        }

        if (criteria.HasSearchableTitle is { } hasSearchableTitle)
        {
            query = hasSearchableTitle
                ? query.Where(i => !i.PendingMetadataRefresh ||
                    context.MetadataContributions
                        .Any(c => c.MetadataContainerId == i.MetadataContainerId && c.Title != null && c.Title != ""))
                : query.Where(i => i.PendingMetadataRefresh &&
                    !context.MetadataContributions
                        .Any(c => c.MetadataContainerId == i.MetadataContainerId && c.Title != null && c.Title != ""));
        }

        query = query.ApplyExactCriteriaSet(criteria.Path, nameof(ContentHierarchyItem.Path));
        query = ApplyTagsCriteria(query, criteria.Tags);
        query = ApplyMetadataCriteria(query, criteria.Metadata);

        if (criteria.Ids is { } criteriaIds)
        {
            var ids = criteriaIds as Guid[] ?? [.. criteriaIds];
            query = query.Where(i => ids.Contains(i.Id));
        }

        if (criteria.ExcludeIds is { Count: > 0 })
        {
            var excludeIds = criteria.ExcludeIds as Guid[] ?? [.. criteria.ExcludeIds];
            query = query.Where(i => !excludeIds.Contains(i.Id));
        }

        return query;
    }

    private IQueryable<ContentHierarchyItem> ApplyMediaFileCriteria(
        IQueryable<ContentHierarchyItem> query,
        CollectionCriteria<MediaFileCriteria> criteria)
    {
        foreach (var group in criteria.Some)
        {
            var groupContentItemIds = ConcatBalanced([.. group.Select(MatchingContentItemIds)]);
            if (groupContentItemIds is not null)
            {
                query = query.Where(i => i.ContentItem != null && groupContentItemIds.Contains(i.ContentItem.Id));
            }
        }

        foreach (var none in criteria.None)
        {
            var noneContentItemIds = MatchingContentItemIds(none);
            query = query.Where(i => i.ContentItem == null || !noneContentItemIds.Contains(i.ContentItem.Id));
        }

        return query;
    }

    private IQueryable<Guid> MatchingContentItemIds(MediaFileCriteria criteria)
    {
        return context.MediaFiles.AsNoTracking()
            .ApplyCriteriaSet(criteria.FileKind, nameof(MediaFile.FileKind))
            .ApplyCriteriaSet(criteria.MimeType, nameof(MediaFile.MimeType))
            .ApplyCriteriaSet(criteria.FilePath, nameof(MediaFile.FilePath))
            .ApplyCriteriaSet(criteria.FileSize, nameof(MediaFile.FileSize))
            .Select(file => file.ContentItemId);
    }

    private IQueryable<ContentHierarchyItem> ApplyMetadataCriteria(
        IQueryable<ContentHierarchyItem> query,
        CriteriaSet<MetadataCriteria> criteria)
    {
        if (criteria.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var metadata in criteria.And)
        {
            query = ApplyMetadataTitleCriteria(query, metadata.Title, isOriginalTitle: false);
            query = ApplyMetadataTitleCriteria(query, metadata.OriginalTitle, isOriginalTitle: true);
            query = ApplyMetadataScalarCriteria(query, metadata);
            query = ApplyRelationshipCriteria(query, metadata.Relationships);
            query = ApplyGenreCriteria(query, metadata.Genres);
            query = ApplyMetadataTagsCriteria(query, metadata.Tags);
            query = ApplyAwardCriteria(query, metadata.Awards);
            query = ApplyExternalIdCriteria(query, metadata.ExternalIds);
            query = ApplyContentRatingCriteria(query, metadata.ContentRatings);
            query = ApplyImageCriteria(query, metadata.Images);
        }

        return query;
    }

    private IQueryable<ContentHierarchyItem> ApplyGenreCriteria(
        IQueryable<ContentHierarchyItem> query,
        CollectionCriteria<GenreCriteria> criteria)
    {
        return ApplyCollectionCriteria(query, criteria, MatchingContainerIdsForGenre);
    }

    private IQueryable<ContentHierarchyItem> ApplyAwardCriteria(
        IQueryable<ContentHierarchyItem> query,
        CollectionCriteria<AwardCriteria> criteria)
    {
        return ApplyCollectionCriteria(query, criteria, MatchingContainerIdsForAward);
    }

    private IQueryable<ContentHierarchyItem> ApplyExternalIdCriteria(
        IQueryable<ContentHierarchyItem> query,
        CollectionCriteria<ExternalIdCriteria> criteria)
    {
        var generic = new CollectionCriteria<ExternalIdCriteria> { None = criteria.None };
        foreach (var group in criteria.Some)
        {
            var keys = ExactExternalIdKeys(group);
            if (keys is null)
            {
                generic.Some.Add(group);
                continue;
            }

            var containerIds = (
                from c in context.MetadataContributions.AsNoTracking()
                join e in DeduplicatedExternalIds() on c.Id equals e.MetadataContributionId
                where keys.Contains(((e.Source ?? string.Empty) + ExternalIdKeySeparator + e.Value).ToLower())
                select c.MetadataContainerId).Distinct();
            query = query.Where(i => containerIds.Contains(i.MetadataContainerId));
        }

        return ApplyCollectionCriteria(query, generic, MatchingContainerIdsByExternalId);
    }

    private IQueryable<ContentHierarchyItem> ApplyContentRatingCriteria(
        IQueryable<ContentHierarchyItem> query,
        CollectionCriteria<ContentRatingCriteria> criteria)
    {
        return ApplyCollectionCriteria(query, criteria, MatchingContainerIdsForContentRating);
    }

    private IQueryable<ContentHierarchyItem> ApplyImageCriteria(
        IQueryable<ContentHierarchyItem> query,
        CollectionCriteria<ImageCriteria> criteria)
    {
        return ApplyCollectionCriteria(query, criteria, MatchingContainerIdsForImage);
    }

    private IQueryable<ContentHierarchyItem> ApplyMetadataTagsCriteria(
        IQueryable<ContentHierarchyItem> query,
        CollectionCriteria<TagCriteria> criteria)
    {
        return ApplyCollectionCriteria(query, criteria, MatchingContainerIdsForTag);
    }

    private IQueryable<Guid> ContainerIdsThroughContributions<TItem>(IQueryable<TItem> items)
        where TItem : class
    {
        return items
            .Join(
                context.MetadataContributions.AsNoTracking(),
                ContributionKeySelector<TItem>(),
                con => con.Id,
                (_, con) => con.MetadataContainerId)
            .Distinct();
    }

    private IQueryable<Guid> MatchingContainerIdsForGenre(GenreCriteria criterion)
    {
        var genres = context.Genres.AsNoTracking()
            .ApplyCriteriaSet(criterion.Name, nameof(Genre.Name));
        return ContainerIdsThroughContributions(genres);
    }

    private IQueryable<Guid> MatchingContainerIdsForAward(AwardCriteria criterion)
    {
        var awards = context.Awards.AsNoTracking()
            .ApplyCriteriaSet(criterion.Body, nameof(Award.Body))
            .ApplyCriteriaSet(criterion.Name, nameof(Award.Name))
            .ApplyCriteriaSet(criterion.Category, nameof(Award.Category))
            .ApplyCriteriaSet(criterion.Status, nameof(Award.Status))
            .ApplyCriteriaSet(criterion.Year, nameof(Award.Year))
            .ApplyCriteriaSet(criterion.Language, nameof(Award.Language))
            .ApplyCriteriaSet(criterion.Country, nameof(Award.Country));
        return ContainerIdsThroughContributions(awards);
    }

    private IQueryable<Guid> MatchingContainerIdsForContentRating(ContentRatingCriteria criterion)
    {
        var ratings = context.ContentRatings.AsNoTracking()
            .ApplyCriteriaSet(criterion.Body, nameof(ContentRating.Body))
            .ApplyCriteriaSet(criterion.Rating, nameof(ContentRating.Rating));
        return ContainerIdsThroughContributions(ratings);
    }

    private IQueryable<Guid> MatchingContainerIdsForImage(ImageCriteria criterion)
    {
        var images = context.Images.AsNoTracking()
            .ApplyCriteriaSet(criterion.Type, nameof(Image.Type))
            .ApplyCriteriaSet(criterion.MimeType, nameof(Image.MimeType))
            .ApplyCriteriaSet(criterion.Source, nameof(Image.Source))
            .ApplyCriteriaSet(criterion.Language, nameof(Image.Language))
            .ApplyCriteriaSet(criterion.Width, nameof(Image.Width))
            .ApplyCriteriaSet(criterion.Height, nameof(Image.Height));
        images = ApplyImageUrlCriteria(images, criterion.Url);
        return ContainerIdsThroughContributions(images);
    }

    private IQueryable<Guid> MatchingContainerIdsForTag(TagCriteria criterion)
    {
        return context.Tags.AsNoTracking()
            .ApplyCriteriaSet(criterion.Name, nameof(Tag.Name))
            .SelectMany(t => t.MetadataContainers)
            .Select(mc => mc.Id)
            .Distinct();
    }

    private IQueryable<ContentHierarchyItem> ApplyRelationshipCriteria(
        IQueryable<ContentHierarchyItem> query,
        CollectionCriteria<RelationshipCriteria> criteria)
    {
        foreach (var group in criteria.Some)
        {
            if (group.Count == 0)
            {
                continue;
            }

            var groupContainerIds = ConcatBalanced(group.Select(MatchingContainerIdsForRelationship).ToList());

            if (groupContainerIds is not null)
            {
                query = query.Where(i => groupContainerIds.Contains(i.MetadataContainerId));
            }
        }

        foreach (var none in criteria.None)
        {
            var noneContainerIds = MatchingContainerIdsForRelationship(none);
            query = query.Where(i => !noneContainerIds.Contains(i.MetadataContainerId));
        }

        return query;
    }

    private IQueryable<Guid> MatchingContainerIdsForRelationship(RelationshipCriteria criterion)
    {
        var relationships = context.Set<Relationship>().AsNoTracking();

        foreach (var containerId in criterion.RelatedMetadataContainerId.And)
        {
            relationships = ApplyRelatedContainerIdPredicate(relationships, containerId);
        }

        foreach (var kind in criterion.Kind.And)
        {
            relationships = relationships.ApplyStringPredicate(kind, r => r.Kind);
        }

        foreach (var qualifier in criterion.Qualifier.And)
        {
            relationships = relationships.ApplyStringPredicate(qualifier, r => r.Qualifier);
        }

        foreach (var type in criterion.Type.And)
        {
            if (type.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
            {
                var typedContainerIds = AllTypedContainerIds();
                relationships = type.Operator == CriteriaValueOperator.IsNotNull
                    ? relationships.Where(r =>
                        r.RelatedMetadataContainerId != null &&
                        typedContainerIds.Contains(r.RelatedMetadataContainerId.Value))
                    : relationships.Where(r =>
                        r.RelatedMetadataContainerId == null ||
                        !typedContainerIds.Contains(r.RelatedMetadataContainerId.Value));
                continue;
            }

            var typeContainerIds = MatchingContainerIdsByType(type);
            relationships = relationships.Where(r =>
                r.RelatedMetadataContainerId != null &&
                typeContainerIds.Contains(r.RelatedMetadataContainerId.Value));
        }

        foreach (var subject in criterion.Subject.And)
        {
            var subjectContainerIds = MatchingSubjectContainerIds(subject);
            var negated = subject.Operator is CriteriaValueOperator.NotContainsString
                or CriteriaValueOperator.NotEquals;
            relationships = negated
                ? relationships.Where(r =>
                    r.RelatedMetadataContainerId == null ||
                    !subjectContainerIds.Contains(r.RelatedMetadataContainerId.Value))
                : relationships.Where(r =>
                    r.RelatedMetadataContainerId != null &&
                    subjectContainerIds.Contains(r.RelatedMetadataContainerId.Value));
        }

        foreach (var externalId in criterion.ExternalIds.SomeFlat)
        {
            var subjectContainerIds = MatchingContainerIdsByExternalId(externalId);
            relationships = relationships.Where(r =>
                r.RelatedMetadataContainerId != null &&
                subjectContainerIds.Contains(r.RelatedMetadataContainerId.Value));
        }

        foreach (var externalId in criterion.ExternalIds.None)
        {
            var subjectContainerIds = MatchingContainerIdsByExternalId(externalId);
            relationships = relationships.Where(r =>
                r.RelatedMetadataContainerId == null ||
                !subjectContainerIds.Contains(r.RelatedMetadataContainerId.Value));
        }

        return relationships
            .Join(
                context.MetadataContributions.AsNoTracking(),
                rel => rel.MetadataContributionId,
                con => con.Id,
                (_, con) => con.MetadataContainerId)
            .Distinct();
    }

    private IQueryable<Guid> AllTypedContainerIds()
    {
        var subjectIds = context.Subjects.AsNoTracking().Select(s => s.MetadataContainerId);
        var contentIds = context.ContentHierarchyItems.AsNoTracking().Select(i => i.MetadataContainerId);
        return subjectIds.Concat(contentIds).Distinct();
    }

    private IQueryable<Guid> MatchingContainerIdsByType(PropertyCriterion<string> criterion)
    {
        var subjectIds = context.Subjects.AsNoTracking()
            .ApplyStringPredicate(criterion, s => s.Kind)
            .Select(s => s.MetadataContainerId);

        var levelIds = context.ContentHierarchyItems.AsNoTracking()
            .ApplyStringPredicate(criterion, i => i.Level)
            .Select(i => i.MetadataContainerId);

        var contentTypeIds = context.ContentHierarchyItems.AsNoTracking()
            .ApplyStringPredicate(criterion, i => i.ContentType)
            .Select(i => i.MetadataContainerId);

        return subjectIds.Concat(levelIds).Concat(contentTypeIds).Distinct();
    }

    private IQueryable<Guid> MatchingSubjectContainerIds(PropertyCriterion<string> criterion)
    {
        var positive = criterion.ToInnerPositive();

        var byTitle = context.MetadataContributions.AsNoTracking()
            .ApplyStringPredicate(positive, c => c.Title)
            .Select(c => c.MetadataContainerId);

        var byAlias = context.AlternateTexts.AsNoTracking().Where(a => a.Kind.ToLower() == "name")
            .ApplyStringPredicate(positive, a => a.Text)
            .Join(
                context.MetadataContributions.AsNoTracking(),
                a => a.MetadataContributionId,
                c => c.Id,
                (_, c) => c.MetadataContainerId);

        return byTitle.Concat(byAlias).Distinct();
    }

    private IQueryable<ContentHierarchyItem> ApplyMetadataTitleCriteria(
        IQueryable<ContentHierarchyItem> query,
        CriteriaSet<PropertyCriterion<string>> criteria,
        bool isOriginalTitle)
    {
        if (criteria.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var criterion in criteria.And)
        {
            var negate = criterion.Operator switch
            {
                CriteriaValueOperator.Equals or CriteriaValueOperator.ContainsString or
                    CriteriaValueOperator.StartsWithString or CriteriaValueOperator.EndsWithString => false,
                CriteriaValueOperator.NotEquals or CriteriaValueOperator.NotContainsString => true,
                var op => throw new NotSupportedException($"Operator {op} is not supported for metadata Title")
            };
            var containerIds = context.MetadataContributions.AsNoTracking().ApplyStringPredicate(
                    criterion.ToInnerPositive(),
                    contribution => isOriginalTitle ? contribution.OriginalTitle : contribution.Title)
                .Select(contribution => contribution.MetadataContainerId);
            query = negate
                ? query.Where(i => !containerIds.Contains(i.MetadataContainerId))
                : query.Where(i => containerIds.Contains(i.MetadataContainerId));
        }

        return query;
    }

    private IQueryable<ExternalId> DeduplicatedExternalIds()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from e in c.ExternalIds
            where c.Id == (
                from c2 in context.MetadataContributions
                from e2 in c2.ExternalIds
                where c2.MetadataContainerId == c.MetadataContainerId
                where e.Source != null
                    ? e2.Source != null && e2.Source.ToLower() == e.Source.ToLower()
                    : e2.Source == null && e2.Value.ToLower() == e.Value.ToLower()
                orderby c2.Ordinal, c2.Id
                select c2.Id).FirstOrDefault()
            orderby c.Ordinal, e.Ordinal
            select e;
    }

    private IQueryable<Guid> MatchingContainerIdsByExternalId(ExternalIdCriteria criterion)
    {
        var externalIds = DeduplicatedExternalIds()
            .ApplyCriteriaSet(criterion.Value, nameof(ExternalId.Value))
            .ApplyCriteriaSet(criterion.Source, nameof(ExternalId.Source));

        return (
            from c in context.MetadataContributions.AsNoTracking()
            join e in externalIds on c.Id equals e.MetadataContributionId
            select c.MetadataContainerId).Distinct();
    }

    private IQueryable<ContentHierarchyItem> ApplyTagsCriteria(
        IQueryable<ContentHierarchyItem> query,
        CriteriaSet<PropertyCriterion<string>> criteria)
    {
        if (!criteria.And.Any())
        {
            return query;
        }

        foreach (var criterion in criteria.And)
        {
            var matchingContainerIds =
                from t in context.Tags.AsNoTracking()
                    .ApplyCriteriaSet(
                        new CriteriaSet<PropertyCriterion<string>> { And = [criterion] }, nameof(Tag.Name))
                from mc in t.MetadataContainers
                select mc.Id;

            query = query.Where(i => matchingContainerIds.Contains(i.MetadataContainerId));
        }

        return query;
    }

    private readonly record struct EffectiveNode(
        Guid Id,
        Guid? ParentId,
        string ContentType,
        string Level,
        Guid MetadataContainerId,
        MonitoringMode? Monitoring,
        Guid? RootFolderId,
        Guid? QualityTierListId,
        MonitoringMode EffectiveMonitoring,
        Guid EffectiveRootFolderId,
        Guid? EffectiveQualityTierListId);

    private readonly record struct ScopeChange(ContentEventImpact? Current, ContentEventImpact? Previous);

    private readonly record struct PatchOutcome(
        bool Applied,
        ContentEventImpact? Current,
        ContentEventImpact? Previous);

    private readonly record struct EffectiveRecompute(
        IReadOnlyDictionary<Guid, IReadOnlyCollection<string>> Changes,
        IReadOnlyDictionary<Guid, EffectiveNode> Nodes);

    private readonly record struct EffectiveValues(
        MonitoringMode Monitoring,
        Guid? RootFolderId,
        Guid? QualityTierListId,
        bool UsesGlobalMonitoring,
        bool UsesLevelDefaultQualityTierList);

    private readonly record struct EffectiveValueTrigger(
        EffectiveValueTriggerKind Kind,
        Guid? ItemId = null,
        Guid? OldParentId = null,
        Guid? NewParentId = null,
        Guid? RootFolderId = null,
        string? ContentType = null);
}
