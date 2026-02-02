using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Wishlist"/>.
/// </summary>
/// <param name="Id">The <see cref="Wishlist.Id"/> of the item to patch.</param>
/// <param name="ContentFilterId">The <see cref="Wishlist.ContentFilterId"/> patch</param>
/// <param name="QualityTierListId">The <see cref="Wishlist.QualityTierListId"/> patch</param>
/// <param name="Name">The <see cref="Wishlist.Name"/> patch</param>
/// <param name="OwnerLock">The <see cref="Wishlist.OwnerLock"/> patch</param>
/// <param name="Tags">The <see cref="Tag"/>s patch</param>
public sealed record WishlistPatch(
    Guid Id,
    ScalarPatch<Guid>? ContentFilterId = null,
    ScalarPatch<Guid?>? QualityTierListId = null,
    ScalarPatch<string>? Name = null,
    ScalarPatch<bool>? OwnerLock = null,
    SetPatch<string, string>? Tags = null) : TagsPatch(Id, Tags);

