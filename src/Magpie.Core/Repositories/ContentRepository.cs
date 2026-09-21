using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Data;
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
    ILogger<ContentRepository> logger) : IContentRepository
{
    private static bool IsFulfilled(FulfillmentStatus status) =>
        status is FulfillmentStatus.Fulfilled or FulfillmentStatus.FulfilledExternally;

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

            return new PagedList<ContentItem>
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
            if (IsFulfilled(contentItem.Fulfillment))
            {
                contentItem.FulfilledAt ??= contentItem.CreatedAt == default
                    ? timeProvider.GetUtcNow().UtcDateTime
                    : contentItem.CreatedAt;
            }

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
                },
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The content item repository failed to update item {ContentItemId}.",
                contentItem.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default)
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

            var fulfilledAt = IsFulfilled(fulfillment)
                ? current is not null && IsFulfilled(current.Fulfillment)
                    ? current.FulfilledAt
                    : now
                : null;

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
                    Monitoring = current.EffectiveMonitoring,
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
                        Monitoring = current.EffectiveMonitoring,
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
            logger.LogError(ex, "The content item repository failed to update the fulfillment of item " +
                "{ContentItemId}.", contentItemId);
            throw;
        }
    }
}
