using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Wishlist"/>.
/// </summary>
public sealed record WishlistPatch : TagsPatch
{
    /// <summary>
    /// The <see cref="Wishlist.ContentFilterId"/> to patch.
    /// </summary>
    public ScalarPatch<Guid>? ContentFilterId { get; init; }

    /// <summary>
    /// The <see cref="Wishlist.QualityTierListId"/> to patch.
    /// </summary>
    public ScalarPatch<Guid?>? QualityTierListId { get; init; }

    /// <summary>
    /// The <see cref="Wishlist.Name"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The <see cref="Wishlist.OwnerLock"/> to patch.
    /// </summary>
    public ScalarPatch<bool>? OwnerLock { get; init; }
}
