using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A tracked request for content that matches a content filter.
/// </summary>
public record Wishlist
{
    /// <summary>
    /// The unique id of the wishlist.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the content filter that selects the wanted content.
    /// </summary>
    public Guid ContentFilterId { get; set; }

    /// <summary>
    /// The id of the quality tier list for the wanted content.
    /// </summary>
    public Guid? QualityTierListId { get; set; }

    /// <summary>
    /// The id of the root folder that receives the content.
    /// </summary>
    public Guid? RootFolderId { get; set; }

    /// <summary>
    /// The id of the user who owns the wishlist.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// The name of the wishlist to show.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Whether only the owner can change the wishlist.
    /// </summary>
    public bool OwnerLock { get; set; }

    /// <summary>
    /// Whether the wishlist is in use.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The UTC time of the last refresh of the wishlist.
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time when the record last changed.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last changed the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }

    /// <summary>
    /// The <see cref="ContentFilter"/> that selects the wanted content.
    /// </summary>
    public ContentFilter? ContentFilter { get; set; }

    /// <summary>
    /// The <see cref="QualityTierList"/> for the wanted content.
    /// </summary>
    public QualityTierList? QualityTierList { get; set; }

    /// <summary>
    /// The <see cref="RootFolder"/> that receives the content.
    /// </summary>
    public RootFolder? RootFolder { get; set; }

    /// <summary>
    /// The tags on the wishlist.
    /// </summary>
    public ICollection<Tag> Tags { get; } = [];
}
