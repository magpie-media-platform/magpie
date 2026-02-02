using System;

namespace Magpie.Models.Data;

/// <summary>
/// Rank of a quality tier within a quality tier list.
/// </summary>
public record QualityTierListRank
{
    /// <summary>
    /// The quality tier list id.
    /// </summary>
    public Guid QualityTierListId { get; set; }

    /// <summary>
    /// The quality tier id.
    /// </summary>
    public Guid QualityTierId { get; set; }

    /// <summary>
    /// The priority order;
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// Whether this tier is a cutoff point for upgrades.
    /// </summary>
    public bool IsCutoff { get; set; }

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
    /// The quality tier.
    /// </summary>
    public QualityTier? QualityTier { get; set; }
}
