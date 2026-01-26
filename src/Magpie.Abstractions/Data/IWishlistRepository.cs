using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;
using Magpie.Models.Wishlists;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing wishlists.
/// </summary>
public interface IWishlistRepository :
    ICrudRepository<Guid, Wishlist>,
    IPatchRepository<Guid, Wishlist, WishlistPatch>
{
    /// <summary>
    /// Get a wishlist by name.
    /// </summary>
    /// <param name="name">The wishlist name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<Wishlist?>> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all wishlists for a specific user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<Wishlist>>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all wishlists that have a specific tag.
    /// </summary>
    /// <param name="tagName">The tag name to filter by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<Wishlist>>> GetByTagAsync(
        string tagName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all wishlists using a specific content filter.
    /// </summary>
    /// <param name="contentFilterId">The content filter identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<Wishlist>>> GetByContentFilterIdAsync(
        Guid contentFilterId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all monitored wishlists.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<Wishlist>>> GetMonitoredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the last synced timestamp for a wishlist.
    /// </summary>
    /// <param name="id">The wishlist identifier.</param>
    /// <param name="lastSyncedAt">The timestamp of the last sync.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<bool>> UpdateLastSyncedAsync(
        Guid id,
        DateTimeOffset lastSyncedAt,
        CancellationToken cancellationToken = default);
}
