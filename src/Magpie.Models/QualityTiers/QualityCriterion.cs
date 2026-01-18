using System;

namespace Magpie.Models.QualityTiers;

/// <summary>
/// A criterion for matching releases to a quality tier.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Field">The field to match (e.g., "resolution", "source", "codec").</param>
/// <param name="Operator">The match operator.</param>
/// <param name="Value">The value or pattern to match against.</param>
public sealed record QualityCriterion(
    Guid Id,
    string Field,
    QualityCriterionOperator Operator,
    string Value);
