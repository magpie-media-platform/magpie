using System;

namespace Magpie.Models.QualityTiers;

/// <summary>
/// Patch for <see cref="QualityCriterion"/>.
/// </summary>
public sealed record QualityCriterionPatch
{
    /// <inheritdoc cref="QualityCriterion.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="QualityCriterion.Field" />
    public FieldPatch<string>? Field { get; init; }
    /// <inheritdoc cref="QualityCriterion.Operator" />
    public FieldPatch<QualityCriterionOperator>? Operator { get; init; }
    /// <inheritdoc cref="QualityCriterion.Value" />
    public FieldPatch<string>? Value { get; init; }
}
