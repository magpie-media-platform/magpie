using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A media content item to be searched and downloaded.
/// </summary>
public record ContentItem
{
    /// <summary>
    /// The content item id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the content hierarchy item the content item belongs to.
    /// </summary>
    public Guid ContentHierarchyItemId { get; set; }

    /// <summary>
    /// The fulfillment status of this item.
    /// </summary>
    public FulfillmentStatus Fulfillment { get; set; }

    /// <summary>
    /// When the content was requested.
    /// </summary>
    public DateTime? RequestedAt { get; set; }

    /// <summary>
    /// The id of the user that requested this content.
    /// </summary>
    public Guid? RequestedByUserId { get; set; }

    /// <summary>
    /// The id of the wishlist that triggered the request.
    /// </summary>
    public Guid? RequestedViaWishlistId { get; set; }

    /// <summary>
    /// The date the item was last searched for new releases.
    /// </summary>
    public DateTime? DateLastSearched { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user that created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// When the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user that last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }

    /// <summary>
    /// The <see cref="MediaFile"/> records associated with this item.
    /// </summary>
    public ICollection<MediaFile> MediaFiles { get; } = [];
}
