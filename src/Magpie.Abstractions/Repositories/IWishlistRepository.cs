using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for wishlists.
/// </summary>
public interface IWishlistRepository :
    IQueryRepository<Wishlist, WishlistQuery>,
    ICrudRepository<Guid, Wishlist>,
    IPatchRepository<Guid, Wishlist, WishlistPatch>;
