using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for wishlists and their discovered ledger.
/// </summary>
public interface IWishlistService
{
    /// <summary>
    /// Lists the shared wishlists and the wishlists of the caller.
    /// </summary>
    /// <param name="monitored">The monitored state to match, or <see langword="null"/> for every wishlist</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<Wishlist>> ListVisibleAsync(
        bool? monitored,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the wishlists that one user owns.
    /// </summary>
    /// <param name="ownerUserId">The id of the user that owns the wishlists</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<Wishlist>> ListOwnAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a wishlist by id when the caller can see it.
    /// </summary>
    /// <param name="wishlistId">The id of the wishlist to return</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Wishlist?> GetAsync(
        Guid wishlistId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a wishlist for an owner.
    /// </summary>
    /// <param name="create">The values for the new wishlist, and whether it needs a reusable filter</param>
    /// <param name="ownerUserId">The id of the user that owns the new wishlist</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Wishlist> CreateAsync(
        WishlistCreate create,
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a wishlist and enforces the owner lock against the caller.
    /// </summary>
    /// <param name="wishlistId">The id of the wishlist to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchAsync(
        Guid wishlistId,
        WishlistPatch patch,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a wishlist and enforces the owner lock against the caller.
    /// </summary>
    /// <param name="wishlistId">The id of the wishlist to delete</param>
    /// <param name="deleteFilter">Whether the delete also removes the backing filter</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteAsync(
        Guid wishlistId,
        bool deleteFilter,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a page of the discovered ledger of a wishlist when the caller can see it.
    /// </summary>
    /// <param name="wishlistId">The id of the wishlist to read</param>
    /// <param name="query">The filter criteria, page number, and page size for the ledger</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<WishlistDiscoveredLedger?> GetDiscoveredAsync(
        Guid wishlistId,
        WishlistDiscoveredQuery query,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);
}
