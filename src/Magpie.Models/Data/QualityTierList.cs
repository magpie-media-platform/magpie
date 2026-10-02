using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A ranked list of quality tiers for release selection.
/// </summary>
public record QualityTierList
{
    /// <summary>
    /// The unique id of the quality tier list.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The name of the list to show.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The MIME patterns that the list applies to, or an empty collection for every MIME type.
    /// </summary>
    public List<string> MimeTypes { get; set; } = [];

    /// <summary>
    /// Whether a release of a higher tier can replace a present one.
    /// </summary>
    public bool UpgradeAllowed { get; set; }

    /// <summary>
    /// The accept and exclude rules that apply to every tier in the list.
    /// </summary>
    public List<QualityTierRule> Rules { get; set; } = [];

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
    /// The rank of each quality tier in the list.
    /// </summary>
    public ICollection<QualityTierListRank> QualityTierRanks { get; set; } = [];
}
