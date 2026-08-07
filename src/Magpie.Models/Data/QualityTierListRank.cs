using System;

namespace Magpie.Models.Data;

/// <summary>
/// The rank of one quality tier within one quality tier list.
/// </summary>
public record QualityTierListRank
{
    /// <summary>
    /// The id of the quality tier list that holds the rank.
    /// </summary>
    public Guid QualityTierListId { get; set; }

    /// <summary>
    /// The id of the ranked quality tier.
    /// </summary>
    public Guid QualityTierId { get; set; }

    /// <summary>
    /// The position of the tier in priority order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// Whether an upgrade stops at this tier.
    /// </summary>
    public bool IsCutoff { get; set; }

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
    /// The <see cref="QualityTier"/> that the rank applies to.
    /// </summary>
    public QualityTier? QualityTier { get; set; }
}
