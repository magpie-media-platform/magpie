using System;

namespace Magpie.Models.QualityTiers;

/// <summary>
/// Patch for <see cref="QualityTier"/>.
/// </summary>
public sealed record QualityTierPatch
{
    /// <inheritdoc cref="QualityTier.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="QualityTier.Ordinal" />
    public FieldPatch<int>? Ordinal { get; init; }
    /// <inheritdoc cref="QualityTier.Name" />
    public FieldPatch<string>? Name { get; init; }
    /// <inheritdoc cref="QualityTier.IsCutoff" />
    public FieldPatch<bool>? IsCutoff { get; init; }
    /// <inheritdoc cref="QualityTier.Criteria" />
    public CollectionPatch<QualityCriterion, Guid, QualityCriterionPatch>? Criteria { get; init; }
}
