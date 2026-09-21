using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Paging;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for content hierarchy items.
/// </summary>
public interface IContentHierarchyItemRepository :
    IQueryRepository<ContentHierarchyItem, ContentHierarchyItemCriteria>,
    ICrudRepository<Guid, ContentHierarchyItem>,
    IPatchRepository<Guid, ContentHierarchyItem, ContentHierarchyItemPatch>,
    IBulkPatchRepository<Guid, ContentHierarchyItem, ContentHierarchyItemCriteria, ContentHierarchyItemPatch>
{
    /// <summary>
    /// Gets an item and each of its parents up to the root.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item to start from</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The line from the item to the root, or an empty list when the item does not exist</returns>
    Task<IReadOnlyList<ContentHierarchyItem>> GetLineageAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries one page of the ids of the items that match, without the read model of the items.
    /// </summary>
    /// <param name="query">The criteria that an item must match</param>
    /// <param name="paging">The page number and page size</param>
    /// <param name="sort">The field and the direction of the sort, or null for the default order</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The ids of the items on the page</returns>
    Task<IReadOnlyList<Guid>> QueryIdsAsync(
        ContentHierarchyItemCriteria query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the parent and the effective monitoring of the items and of every ancestor above them.
    /// </summary>
    /// <param name="contentHierarchyItemIds">The ids of the items to start from</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The ancestry of each item, keyed by item id</returns>
    Task<IReadOnlyDictionary<Guid, ContentHierarchyItemAncestry>> GetAncestryAsync(
        IReadOnlyCollection<Guid> contentHierarchyItemIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks an item until the current transaction completes.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item to lock</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task LockAsync(Guid contentHierarchyItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an item, or does nothing when its unique key exists already.
    /// </summary>
    /// <param name="model">The content hierarchy item to create</param>
    /// <param name="createdByUserId">The id of the user that creates the item, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the item is new, otherwise <see langword="false"/></returns>
    Task<bool> TryCreateAsync(
        ContentHierarchyItem model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the counts that summarize the content library.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The current content stats</returns>
    Task<ContentStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an item with its subtree and reports the ids that the delete removed.
    /// </summary>
    /// <param name="id">The id of the item at the top of the subtree</param>
    /// <param name="deletedByUserId">The id of the user that deletes the records</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The ids of the rows the delete removed, empty when the item does not exist</returns>
    Task<IReadOnlyList<Guid>> DeleteSubtreeAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every item that the bulk selection matches, with the subtree of each.
    /// </summary>
    /// <param name="action">The selection of items to delete</param>
    /// <param name="deletedByUserId">The id of the user that deletes the records</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The outcome of the bulk deletion and the ids of the rows the delete removed</returns>
    Task<(BulkResult Result, IReadOnlyList<Guid> DeletedIds)> BulkDeleteAsync(
        BulkAction<ContentHierarchyItemCriteria> action,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the number of direct children of each parent.
    /// </summary>
    /// <param name="parentIds">The ids of the parent content hierarchy items</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The child count for each parent id</returns>
    Task<IReadOnlyDictionary<Guid, int>> GetChildCountsByParentAsync(
        IReadOnlyCollection<Guid> parentIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes the effective values of every item.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task RecomputeAllEffectiveValuesAsync(CancellationToken cancellationToken = default);
}
