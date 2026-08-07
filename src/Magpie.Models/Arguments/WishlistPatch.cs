using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a wishlist.
/// </summary>
public sealed record WishlistPatch : TagsPatch
{
    /// <summary>
    /// The patch for the id of the saved content filter.
    /// </summary>
    public ScalarPatch<Guid?>? ContentFilterId { get; init; }

    /// <summary>
    /// The patch for the content filter criteria.
    /// </summary>
    public ScalarPatch<ContentFilterCriteria>? Criteria { get; init; }

    /// <summary>
    /// The patch for whether to save the criteria as a reusable filter.
    /// </summary>
    public bool? SaveAsReusableFilter { get; init; }

    /// <summary>
    /// The patch for the id of the quality tier list.
    /// </summary>
    public ScalarPatch<Guid?>? QualityTierListId { get; init; }

    /// <summary>
    /// The patch for the id of the root folder.
    /// </summary>
    public ScalarPatch<Guid?>? RootFolderId { get; init; }

    /// <summary>
    /// The patch for the display name of the wishlist.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for whether only the owner can edit the wishlist.
    /// </summary>
    public ScalarPatch<bool>? OwnerLock { get; init; }

    /// <summary>
    /// The patch for whether the wishlist is enabled.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }

    /// <summary>
    /// The patch for the UTC time of the last sync.
    /// </summary>
    public ScalarPatch<DateTime?>? LastSyncedAt { get; init; }
}
