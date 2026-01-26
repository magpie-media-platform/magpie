using System;

namespace Magpie.Models.Wishlists;

/// <summary>
/// Update model for setting the last sync timestamp on a wishlist.
/// </summary>
/// <param name="LastSyncedAt">The timestamp of the last sync.</param>
public sealed record WishlistSyncUpdate(DateTimeOffset LastSyncedAt);
