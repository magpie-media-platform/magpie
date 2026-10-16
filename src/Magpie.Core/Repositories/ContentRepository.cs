using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ContentEventImpact = Magpie.Sdk.Events.ContentEventImpact;

namespace Magpie.Core.Repositories;

internal sealed class ContentRepository(
    MagpieDbContext context,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    TimeProvider timeProvider,
    ILogger<ContentRepository> logger,
    IContentTypeRegistry contentTypeRegistry,
    IFileSystem fileSystem,
    IAuditRecordRepository auditRecordRepository) : IContentRepository
{
    private static readonly (string Name, Func<ContentItem, ContentItem, bool> Changed)[] UpdateFields =
    [
        (nameof(ContentItem.FulfilledAt), (current, model) => current.FulfilledAt != model.FulfilledAt),
        (nameof(ContentItem.QualityStatus), (current, model) => current.QualityStatus != model.QualityStatus),
        (nameof(ContentItem.DateLastSearched), (current, model) => current.DateLastSearched != model.DateLastSearched),
    ];

    private static readonly (string Name, Func<ContentItem, ContentItem, bool> Changed)[] MediaStateFields =
    [
        (nameof(ContentItem.FulfilledAt), (current, model) => current.FulfilledAt != model.FulfilledAt),
        (nameof(ContentItem.QualityStatus), (current, model) => current.QualityStatus != model.QualityStatus),
        (nameof(ContentItem.PrimaryMediaSize), (current, model) => current.PrimaryMediaSize != model.PrimaryMediaSize),
        (nameof(ContentItem.PrimaryMediaFileCount),
            (current, model) => current.PrimaryMediaFileCount != model.PrimaryMediaFileCount),
        (nameof(ContentItem.CurrentFileQualityRank),
            (current, model) => current.CurrentFileQualityRank != model.CurrentFileQualityRank),
    ];

    private static bool IsFulfilled(FulfillmentStatus status) =>
        status is not FulfillmentStatus.Missing;

    private static List<string> ChangedFields(
        (string Name, Func<ContentItem, ContentItem, bool> Changed)[] fields,
        ContentItem current,
        ContentItem model) =>
        [.. fields.Where(field => field.Changed(current, model)).Select(field => field.Name)];

    private IQueryable<ContentItem> ApplySort(IQueryable<ContentItem> items, SortParameter? sort)
    {
        var property = context.Model.FindEntityType(typeof(ContentItem))!.GetProperties()
            .Where(candidate => !candidate.IsShadowProperty() &&
                (candidate.ClrType.IsValueType || candidate.ClrType == typeof(string) ||
                    candidate.ClrType == typeof(Uri)))
            .Select(candidate => candidate.Name)
            .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
        if (sort is null || property is null)
        {
            return items.OrderBy(item => item.Id);
        }

        return sort.Direction == SortDirection.Ascending
            ? items.OrderBy(item => EF.Property<object>(item, property)).ThenBy(item => item.Id)
            : items.OrderByDescending(item => EF.Property<object>(item, property)).ThenBy(item => item.Id);
    }

    public async Task<IEnumerable<ContentItem>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.ContentItems.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The content item repository failed to list the items.");
            throw;
        }
    }

    public async Task<ContentItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.ContentItems.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The content item repository failed to read item {ContentItemId}.", id);
            throw;
        }
    }

    public async Task<PagedList<ContentItem>> QueryAsync(
        ContentItemQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var items = context.ContentItems.AsNoTracking().AsQueryable();
            if (query.ContentHierarchyItemId is { } hierarchyItemId)
            {
                items = items.Where(item => item.ContentHierarchyItemId == hierarchyItemId);
            }

            if (query.ContentHierarchyItemIds is { } hierarchyItemIds)
            {
                items = items.Where(item => hierarchyItemIds.Contains(item.ContentHierarchyItemId));
            }

            items = ApplySort(items, sort);
            var totalItems = await items.CountAsync(cancellationToken).ConfigureAwait(false);
            items = items.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            return new()
            {
                Items = await items.ToListAsync(cancellationToken).ConfigureAwait(false),
                TotalItems = totalItems,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The content item repository failed to query the items.");
            throw;
        }
    }

    public async Task<ContentItem> CreateAsync(
        ContentItem contentItem,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            contentItem.CreatedByUserId = createdByUserId;
            context.ContentItems.Add(contentItem);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return contentItem;
        }
        catch (Exception ex)
        {
            if (ex is DbUpdateException)
            {
                context.ChangeTracker.Clear();
            }

            logger.LogError(
                ex,
                "The content item repository failed to create the item for hierarchy item {ContentHierarchyItemId}.",
                contentItem.ContentHierarchyItemId);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        ContentItem contentItem,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var current = await GetByIdAsync(contentItem.Id, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                return false;
            }

            var now = contentItem.ModifiedAt ?? timeProvider.GetUtcNow().UtcDateTime;
            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    contentItem.FulfilledAt = await UpdateFulfillmentAsync(
                        contentItem.Id,
                        contentItem.Fulfillment,
                        now,
                        modifiedByUserId,
                        ct).ConfigureAwait(false);
                    await context.ContentItems
                        .Where(item => item.Id == contentItem.Id)
                        .ExecuteUpdateAsync(
                            update => update
                                .SetProperty(item => item.QualityStatus, contentItem.QualityStatus)
                                .SetProperty(item => item.DateLastSearched, contentItem.DateLastSearched)
                                .SetProperty(item => item.ModifiedAt, now)
                                .SetProperty(item => item.ModifiedByUserId, modifiedByUserId),
                            ct)
                        .ConfigureAwait(false);
                    await PublishUpdatedAsync(contentItem.Id, ChangedFields(UpdateFields, current, contentItem), ct)
                        .ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The content item repository failed to update item {ContentItemId}.", contentItem.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateMediaStateAsync(
        ContentItem contentItem,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var current = await GetByIdAsync(contentItem.Id, ct).ConfigureAwait(false);
                    await UpdateFulfillmentAsync(
                        contentItem.Id,
                        contentItem.Fulfillment,
                        contentItem.ModifiedAt ?? timeProvider.GetUtcNow().UtcDateTime,
                        modifiedByUserId,
                        ct).ConfigureAwait(false);
                    var fulfilledAt = contentItem.Fulfillment == FulfillmentStatus.Missing
                        ? null
                        : contentItem.FulfilledAt;
                    var updated = await context.ContentItems
                        .Where(item => item.Id == contentItem.Id)
                        .ExecuteUpdateAsync(
                            update => update
                                .SetProperty(item => item.FulfilledAt, fulfilledAt)
                                .SetProperty(item => item.QualityStatus, contentItem.QualityStatus)
                                .SetProperty(item => item.PrimaryMediaSize, contentItem.PrimaryMediaSize)
                                .SetProperty(item => item.PrimaryMediaFileCount, contentItem.PrimaryMediaFileCount)
                                .SetProperty(item => item.CurrentFileQualityRank, contentItem.CurrentFileQualityRank)
                                .SetProperty(item => item.MediaReconciledAt, contentItem.MediaReconciledAt),
                            ct).ConfigureAwait(false) > 0;
                    if (updated && current is not null)
                    {
                        await PublishUpdatedAsync(
                            contentItem.Id,
                            ChangedFields(MediaStateFields, current, contentItem with { FulfilledAt = fulfilledAt }),
                            ct).ConfigureAwait(false);
                    }

                    return updated;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The content repository failed to save media state for {ContentItemId}.",
                contentItem.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.ContentItems.Where(item => item.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false) > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The content item repository failed to delete item {ContentItemId}.", id);
            throw;
        }
    }

    private async Task<DateTime?> UpdateFulfillmentAsync(
        Guid contentItemId,
        FulfillmentStatus fulfillment,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var current = await (
                from c in context.ContentItems.AsNoTracking()
                join i in context.ContentHierarchyItems.AsNoTracking()
                    on c.ContentHierarchyItemId equals i.Id
                where c.Id == contentItemId
                select new
                {
                    c.Fulfillment,
                    c.FulfilledAt,
                    c.RequestedViaWishlistId,
                    i.Id,
                    i.MetadataContainerId,
                    i.ParentId,
                    i.ContentType,
                    i.Level,
                    i.RootFolderId,
                    i.QualityTierListId,
                    i.EffectiveMonitoring
                })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var fulfilledAt = IsFulfilled(fulfillment) ? current?.FulfilledAt : null;

            await context.ContentItems
                .Where(c => c.Id == contentItemId)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(c => c.Fulfillment, fulfillment)
                        .SetProperty(c => c.FulfilledAt, fulfilledAt)
                        .SetProperty(c => c.ModifiedAt, (DateTime?)now)
                        .SetProperty(c => c.ModifiedByUserId, userId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (current is not null && current.Fulfillment != fulfillment)
            {
                var from = current.Fulfillment;
                var impact = new ContentEventImpact
                {
                    HierarchyItemId = current.Id,
                    ItemId = contentItemId,
                    MetadataContainerId = current.MetadataContainerId,
                    ParentId = current.ParentId,
                    ContentType = current.ContentType,
                    Level = current.Level,
                    TopLevel = current.ParentId is null,
                    RootFolderId = current.RootFolderId,
                    QualityTierListId = current.QualityTierListId,
                    Monitoring = current.EffectiveMonitoring != MonitoringMode.Unmonitored,
                    Fulfillment = fulfillment.ToString(),
                    WishlistId = current.RequestedViaWishlistId,
                    Previous = new()
                    {
                        MetadataContainerId = current.MetadataContainerId,
                        ParentId = current.ParentId,
                        ContentType = current.ContentType,
                        Level = current.Level,
                        TopLevel = current.ParentId is null,
                        RootFolderId = current.RootFolderId,
                        QualityTierListId = current.QualityTierListId,
                        Monitoring = current.EffectiveMonitoring != MonitoringMode.Unmonitored,
                        Fulfillment = from.ToString()
                    }
                };
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ContentEventBuilder.BuildContentFulfillmentChangedEvent(impact, from, fulfillment),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return fulfilledAt;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The content item repository failed to update the fulfillment of item {ContentItemId}.",
                contentItemId);
            throw;
        }
    }

    private async Task PublishUpdatedAsync(
        Guid contentItemId,
        IReadOnlyCollection<string> changedFields,
        CancellationToken cancellationToken)
    {
        if (changedFields.Count == 0)
        {
            return;
        }

        var current = await (
            from c in context.ContentItems.AsNoTracking()
            join i in context.ContentHierarchyItems.AsNoTracking()
                on c.ContentHierarchyItemId equals i.Id
            where c.Id == contentItemId
            select new
            {
                c.Fulfillment,
                c.RequestedViaWishlistId,
                i.Id,
                i.MetadataContainerId,
                i.ParentId,
                i.ContentType,
                i.Level,
                i.RootFolderId,
                i.QualityTierListId,
                i.EffectiveMonitoring
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return;
        }

        var impact = new ContentEventImpact
        {
            HierarchyItemId = current.Id,
            ItemId = contentItemId,
            MetadataContainerId = current.MetadataContainerId,
            ParentId = current.ParentId,
            ContentType = current.ContentType,
            Level = current.Level,
            TopLevel = current.ParentId is null,
            RootFolderId = current.RootFolderId,
            QualityTierListId = current.QualityTierListId,
            Monitoring = current.EffectiveMonitoring != MonitoringMode.Unmonitored,
            Fulfillment = current.Fulfillment.ToString(),
            WishlistId = current.RequestedViaWishlistId
        };
        await transactionManager.OnCommittedAsync(
            token => eventBus.PublishAsync(
                ContentEventBuilder.BuildContentHierarchyItemUpdatedEvent(impact, changedFields),
                token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReconcileMediaStateAsync(
        Guid hierarchyItemId,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nestedTransaction = context.Database.CurrentTransaction is not null;
            var completed = false;
            while (!completed)
            {
                completed = await transactionManager.ExecuteWithTransactionAsync(async ct =>
                {
                    var rootId = hierarchyItemId;
                    var visited = new HashSet<Guid>();
                    while (visited.Add(rootId))
                    {
                        var row = await context.ContentHierarchyItems.AsNoTracking()
                            .Where(item => item.Id == rootId).Select(item => new { item.ParentId })
                            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
                        if (row is null)
                        {
                            return true;
                        }

                        if (row.ParentId is not { } parentId)
                        {
                            break;
                        }

                        rootId = parentId;
                    }

                    await context.ContentHierarchyItems.Where(item => item.Id == rootId)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(item => item.ModifiedAt, item => item.ModifiedAt),
                            ct)
                        .ConfigureAwait(false);

                    var lockedRootId = hierarchyItemId;
                    visited.Clear();
                    while (visited.Add(lockedRootId))
                    {
                        var row = await context.ContentHierarchyItems.AsNoTracking()
                            .Where(item => item.Id == lockedRootId).Select(item => new { item.ParentId })
                            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
                        if (row is null)
                        {
                            return true;
                        }

                        if (row.ParentId is not { } parentId)
                        {
                            break;
                        }

                        lockedRootId = parentId;
                    }

                    if (lockedRootId != rootId)
                    {
                        if (nestedTransaction)
                        {
                            throw new DbUpdateConcurrencyException(
                                "The media hierarchy changed during reconciliation.");
                        }

                        return false;
                    }

                    var nodes = await context.ContentHierarchyItems.AsNoTracking()
                        .Where(item => item.Id == rootId)
                        .Include(item => item.ContentItem).ThenInclude(item => item!.MediaFiles)
                        .Include(item => item.ContentItem).ThenInclude(item => item!.CoveringMediaFiles)
                        .ToListAsync(ct).ConfigureAwait(false);
                    for (var offset = 0; offset < nodes.Count;)
                    {
                        var ids = nodes.Skip(offset).Select(item => item.Id).ToArray();
                        offset = nodes.Count;
                        nodes.AddRange(await context.ContentHierarchyItems.AsNoTracking()
                            .Where(item => item.ParentId != null && ids.Contains(item.ParentId.Value))
                            .Include(item => item.ContentItem).ThenInclude(item => item!.MediaFiles)
                            .Include(item => item.ContentItem).ThenInclude(item => item!.CoveringMediaFiles)
                            .ToListAsync(ct).ConfigureAwait(false));
                    }

                    var children = nodes.Where(item => item.ParentId != null).ToLookup(item => item.ParentId!.Value);
                    var primaryFiles = new Dictionary<Guid, Dictionary<Guid, long>>();
                    foreach (var node in nodes.AsEnumerable().Reverse())
                    {
                        var level = contentTypeRegistry.GetDefinitionFor(node.ContentType)?.LevelDefinitions
                            .FirstOrDefault(item => item.Slug == node.Level);
                        var now = timeProvider.GetUtcNow().UtcDateTime;
                        var current = node.ContentItem ?? await CreateAsync(new ContentItem
                        {
                            ContentHierarchyItemId = node.Id,
                            CreatedAt = now,
                            Fulfillment = FulfillmentStatus.Missing
                        }, modifiedByUserId, ct).ConfigureAwait(false);
                        if (level is null || children[node.Id].Any(item => item.ContentItem?.MediaReconciledAt == null))
                        {
                            node.ContentItem = current with { MediaReconciledAt = null };
                            if (current.MediaReconciledAt != null)
                            {
                                await UpdateMediaStateAsync(node.ContentItem, modifiedByUserId, ct)
                                    .ConfigureAwait(false);
                            }

                            continue;
                        }

                        var files = current.MediaFiles.Concat(current.CoveringMediaFiles)
                            .Where(file => FileSystem.IsPrimaryFile(fileSystem, level, file))
                            .ToList();
                        var subtreeFiles = children[node.Id].SelectMany(item => primaryFiles[item.Id])
                            .Concat(files.Select(file => KeyValuePair.Create(file.Id, file.FileSize)))
                            .DistinctBy(file => file.Key)
                            .ToDictionary();
                        primaryFiles[node.Id] = subtreeFiles;

                        var childItems = children[node.Id].Select(item => item.ContentItem!).ToList();
                        var states = childItems.Select(item => item.Fulfillment).ToList();
                        if (files.Count > 0 || childItems.Count == 0)
                        {
                            states.Add(files.Count == 0 ? FulfillmentStatus.Missing
                                : current.QualityStatus == QualityStatus.UpgradeWanted
                                    ? FulfillmentStatus.Upgradable : FulfillmentStatus.Fulfilled);
                        }

                        var fulfillment = current.Fulfillment == FulfillmentStatus.FulfilledExternally
                            ? current.Fulfillment
                            : states.All(state => state == FulfillmentStatus.Missing) ? FulfillmentStatus.Missing
                            : states.Any(state => state is FulfillmentStatus.Missing or FulfillmentStatus.Partial)
                                ? FulfillmentStatus.Partial
                            : states.Contains(FulfillmentStatus.Upgradable) ? FulfillmentStatus.Upgradable
                            : FulfillmentStatus.Fulfilled;
                        var dates = files.Select(file => file.ReplacedAt ??
                                (file.CreatedAt == default ? (DateTime?)null : file.CreatedAt))
                            .Append(files.Count > 0 && files.All(file =>
                                file.ReplacedAt == null && file.CreatedAt == default)
                                ? current.FulfilledAt : null)
                            .Concat(childItems.Where(item => item.Fulfillment != FulfillmentStatus.FulfilledExternally)
                                .Select(item => item.FulfilledAt));
                        var date = fulfillment == FulfillmentStatus.Missing ? null
                            : fulfillment == FulfillmentStatus.FulfilledExternally ? current.FulfilledAt : dates.Max();
                        var size = subtreeFiles.Values.Sum();
                        var count = subtreeFiles.Count;
                        node.ContentItem = current with
                        {
                            Fulfillment = fulfillment,
                            FulfilledAt = date,
                            PrimaryMediaSize = size,
                            PrimaryMediaFileCount = count,
                            MediaReconciledAt = now,
                            ModifiedAt = now
                        };
                        if (current.Fulfillment == fulfillment && current.FulfilledAt == date &&
                            current.PrimaryMediaSize == size && current.PrimaryMediaFileCount == count &&
                            current.MediaReconciledAt != null)
                        {
                            continue;
                        }

                        await UpdateMediaStateAsync(node.ContentItem, modifiedByUserId, ct).ConfigureAwait(false);
                        await auditRecordRepository.CreateAsync(new AuditRecord
                        {
                            Category = AuditCategories.Content,
                            Operation = "MediaStateReconciled",
                            EntityType = nameof(ContentItem),
                            EntityId = current.Id.ToString(),
                            ParentEntityId = node.Id.ToString(),
                            Detail = $"Reconciled {count} primary media files and {size} bytes.",
                            Timestamp = now,
                            UserId = modifiedByUserId
                        }, ct).ConfigureAwait(false);
                    }

                    return true;
                }, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The content repository failed to reconcile media state for {HierarchyItemId}.",
                hierarchyItemId);
            throw;
        }
    }
}
