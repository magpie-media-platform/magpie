using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A ranked list of quality tiers for release selection.
/// </summary>
public record QualityTierList
{
    /// <summary>
    /// The quality tier list id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Whether upgrading to higher quality is allowed.
    /// </summary>
    public bool UpgradeAllowed { get; set; }

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
    /// The quality tiers in this list.
    /// </summary>
    public ICollection<QualityTierListRank> QualityTierRanks { get; } = [];
}
