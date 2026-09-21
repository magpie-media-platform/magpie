using System;

using Magpie.Models.Data;

namespace Magpie.Models.Arguments;

/// <summary>
/// The create arguments for a <see cref="Wishlist"/>.
/// </summary>
public sealed record WishlistCreate
{
    /// <summary>
    /// The display name of the wishlist.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The id of the saved content filter to use.
    /// </summary>
    public Guid? ContentFilterId { get; init; }

    /// <summary>
    /// The content filter criteria for the wishlist.
    /// </summary>
    public ContentFilterCriteria? Criteria { get; init; }

    /// <summary>
    /// Whether to save the criteria as a reusable filter.
    /// </summary>
    public bool SaveAsReusableFilter { get; init; }

    /// <summary>
    /// The id of the quality tier list for discovered items.
    /// </summary>
    public Guid? QualityTierListId { get; init; }

    /// <summary>
    /// The id of the root folder for discovered items.
    /// </summary>
    public Guid? RootFolderId { get; init; }

    /// <summary>
    /// Whether the wishlist is enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Whether added content is monitored, or <see langword="null"/> to inherit.
    /// </summary>
    public bool? Monitoring { get; init; }

    /// <summary>
    /// Whether only the owner can edit the wishlist.
    /// </summary>
    public bool OwnerLock { get; init; }
}
