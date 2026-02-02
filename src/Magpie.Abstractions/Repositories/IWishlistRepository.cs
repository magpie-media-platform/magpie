using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing wishlists.
/// </summary>
public interface IWishlistRepository :
    ICrudRepository<Guid, Wishlist>,
    IPatchRepository<Guid, Wishlist, WishlistPatch>
{
    /// <summary>
    /// Get all wishlists for a specific user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<Wishlist>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all monitored wishlists.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<Wishlist>> GetMonitoredAsync(CancellationToken cancellationToken = default);
}
