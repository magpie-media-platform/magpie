using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for content items and content aggregate rows.
/// </summary>
public interface IContentRepository :
    ICrudRepository<Guid, ContentItem>,
    IQueryRepository<ContentItem, ContentItemQuery>
{
    /// <summary>
    /// Recomputes stored media totals and dates for an item and its hierarchy.
    /// </summary>
    /// <param name="hierarchyItemId">An item in the affected hierarchy</param>
    /// <param name="modifiedByUserId">The user who changed the media, or null for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task ReconcileMediaStateAsync(
        Guid hierarchyItemId,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the fulfillment, quality, date and totals derived from current media files.
    /// </summary>
    /// <param name="contentItem">The content item with the recomputed media state</param>
    /// <param name="modifiedByUserId">The user who requested reconciliation, or null for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> UpdateMediaStateAsync(
        ContentItem contentItem,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);
}
