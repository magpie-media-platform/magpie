using System;
using System.Collections.Generic;

namespace Magpie.Models.QualityTiers;

/// <summary>
/// A prioritized list of quality tiers for release selection.
/// </summary>
/// <remarks>
/// Tiers are evaluated in <see cref="QualityTier.Ordinal"/> order and the first match wins.
/// When <see cref="QualityTier.IsCutoff"/> is true on a tier, items reaching that tier stop upgrading.
/// </remarks>
/// <param name="Id">The unique identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Tiers">The quality tiers in priority order (highest first).</param>
/// <param name="UpgradeAllowed">Whether upgrades are allowed for items using this list.</param>
/// <param name="Tags">Tags applied to this quality tier list.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record QualityTierList(
    Guid Id,
    string Name,
    IReadOnlyList<QualityTier> Tiers,
    bool UpgradeAllowed,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
