using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="QualityTierList"/>.
/// </summary>
/// <param name="Id">The <see cref="QualityTierList.Id"/> of the item to patch.</param>
/// <param name="Name">The <see cref="QualityTierList.Name"/> patch</param>
/// <param name="UpgradeAllowed">The <see cref="QualityTierList.UpgradeAllowed"/> patch</param>
/// <param name="QualityTierRanks">The <see cref="QualityTierList.QualityTierRanks"/> patch</param>
/// <param name="Tags">The <see cref="Tag"/>s patch</param>
public sealed record QualityTierListPatch(
    Guid Id,
    ScalarPatch<string>? Name = null,
    ScalarPatch<bool>? UpgradeAllowed = null,
    CollectionPatch<QualityTierListRankCreate, Guid, QualityTierListRankPatch>? QualityTierRanks = null,
    SetPatch<string, string>? Tags = null) : TagsPatch(Id, Tags);
