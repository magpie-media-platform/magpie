using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="QualityTierList"/>.
/// </summary>
public sealed record QualityTierListPatch : TagsPatch
{
    /// <summary>
    /// The <see cref="QualityTierList.Name"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The <see cref="QualityTierList.UpgradeAllowed"/> to patch.
    /// </summary>
    public ScalarPatch<bool>? UpgradeAllowed { get; init; }

    /// <summary>
    /// The <see cref="QualityTierList.QualityTierRanks"/> to patch.
    /// </summary>
    public CollectionPatch<QualityTierListRankCreate, Guid, QualityTierListRankPatch>? QualityTierRanks { get; init; }
}
