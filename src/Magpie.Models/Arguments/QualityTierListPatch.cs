using System;
using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a quality tier list.
/// </summary>
public sealed record QualityTierListPatch : TagsPatch
{
    /// <summary>
    /// The patch for the display name of the list.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for the MIME patterns.
    /// </summary>
    public ScalarPatch<List<string>>? MimeTypes { get; init; }

    /// <summary>
    /// The patch for whether upgrades are allowed.
    /// </summary>
    public ScalarPatch<bool>? UpgradeAllowed { get; init; }

    /// <summary>
    /// The patch for the ranks of the quality tiers in the list.
    /// </summary>
    public CollectionPatch<QualityTierListRankCreate, Guid, QualityTierListRankPatch>? QualityTierRanks { get; init; }

    /// <summary>
    /// The patch for the accept and exclude rules of the list.
    /// </summary>
    public ScalarPatch<List<QualityTierRule>>? Rules { get; init; }
}
