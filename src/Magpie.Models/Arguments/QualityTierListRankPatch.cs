using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="QualityTierListRank"/>.
/// </summary>
/// <param name="QualityTierId">The <see cref="QualityTierListRank.QualityTierId"/> patch</param>
/// <param name="Ordinal">The <see cref="QualityTierListRank.Ordinal"/> patch</param>
/// <param name="IsCutoff">The <see cref="QualityTierListRank.IsCutoff"/> patch</param>
public sealed record QualityTierListRankPatch(
    ScalarPatch<Guid>? QualityTierId = null,
    ScalarPatch<int>? Ordinal = null,
    ScalarPatch<bool>? IsCutoff = null);
