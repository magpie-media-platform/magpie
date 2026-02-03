using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="QualityTierListRank"/>.
/// </summary>
public sealed record QualityTierListRankPatch
{
    /// <summary>
    /// The <see cref="QualityTierListRank.QualityTierId"/> to patch.
    /// </summary>
    public ScalarPatch<Guid>? QualityTierId { get; init; }

    /// <summary>
    /// The <see cref="QualityTierListRank.Ordinal"/> to patch.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The <see cref="QualityTierListRank.IsCutoff"/> to patch.
    /// </summary>
    public ScalarPatch<bool>? IsCutoff { get; init; }
}
