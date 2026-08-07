using System;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a quality tier list rank.
/// </summary>
public sealed record QualityTierListRankPatch
{
    /// <summary>
    /// The patch for the id of the quality tier.
    /// </summary>
    public ScalarPatch<Guid>? QualityTierId { get; init; }

    /// <summary>
    /// The patch for the position in the display order.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The patch for whether the tier is the cutoff point for upgrades.
    /// </summary>
    public ScalarPatch<bool>? IsCutoff { get; init; }
}
