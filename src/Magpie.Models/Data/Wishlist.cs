using System;

namespace Magpie.Models.Data;

/// <summary>
/// A wishlist for tracking desired content.
/// </summary>
public record Wishlist
{
    /// <summary>
    /// The wishlist id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The content filter used to match releases.
    /// </summary>
    public Guid ContentFilterId { get; set; }

    /// <summary>
    /// The quality tier list for ranking releases.
    /// </summary>
    public Guid? QualityTierListId { get; set; }

    /// <summary>
    /// The id of the user that owns this wishlist.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// The display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Whether the wishlist is locked to and only editable by the owner.
    /// </summary>
    public bool OwnerLock { get; set; }

    /// <summary>
    /// Whether the wishlist is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// When the wishlist was last synchronized.
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }

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
    /// The <see cref="ContentFilter"/> that matches releases.
    /// </summary>
    public ContentFilter? ContentFilter { get; set; }

    /// <summary>
    /// The <see cref="QualityTierList"/> that ranks releases.
    /// </summary>
    public QualityTierList? QualityTierList { get; set; }
}
