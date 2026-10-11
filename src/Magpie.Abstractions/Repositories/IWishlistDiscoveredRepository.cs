using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for the candidates that a wishlist discovered.
/// </summary>
public interface IWishlistDiscoveredRepository : IRepository
{
    /// <summary>
    /// Lists the candidates that a wishlist discovered.
    /// </summary>
    /// <param name="wishlistId">The id of the wishlist</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The discovered candidates</returns>
    Task<IReadOnlyCollection<WishlistDiscovered>> ListAsync(
        Guid wishlistId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a set of candidates as discovered for a wishlist.
    /// </summary>
    /// <param name="wishlistId">The id of the wishlist</param>
    /// <param name="candidates">The candidates to record</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task RecordDiscoveredAsync(
        Guid wishlistId,
        IEnumerable<WishlistDiscovered> candidates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one page of the discovered items of a wishlist, with their enriched fields.
    /// </summary>
    /// <param name="wishlistId">The id of the wishlist</param>
    /// <param name="query">The criteria that an item must match</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>A page of the enriched discovered items</returns>
    Task<PagedList<WishlistDiscoveredEnrichedRow>> ListEnrichedAsync(
        Guid wishlistId,
        WishlistDiscoveredQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the counts of the discovered items of a wishlist.
    /// </summary>
    /// <param name="wishlistId">The id of the wishlist</param>
    /// <param name="titleSearch">The text that a title must hold, or <c>null</c> for every item</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The counts of the discovered items</returns>
    Task<WishlistDiscoveredCounts> GetCountsAsync(
        Guid wishlistId,
        string? titleSearch,
        CancellationToken cancellationToken = default);
}
