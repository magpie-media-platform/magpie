using System;

namespace Magpie.Models.QualityTiers;

/// <summary>
/// Patch for <see cref="QualityTierList"/>.
/// </summary>
public sealed record QualityTierListPatch
{
    /// <inheritdoc cref="QualityTierList.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="QualityTierList.Name" />
    public FieldPatch<string>? Name { get; init; }
    /// <inheritdoc cref="QualityTierList.Tiers" />
    public CollectionPatch<QualityTier, Guid, QualityTierPatch>? Tiers { get; init; }
    /// <inheritdoc cref="QualityTierList.UpgradeAllowed" />
    public FieldPatch<bool>? UpgradeAllowed { get; init; }
    /// <inheritdoc cref="QualityTierList.Tags" />
    public SetPatch<string, string>? Tags { get; init; }
}
