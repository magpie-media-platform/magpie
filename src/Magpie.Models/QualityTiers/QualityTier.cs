using System;
using System.Collections.Generic;

namespace Magpie.Models.QualityTiers;

/// <summary>
/// A single quality tier within a tier list.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Ordinal">The priority order (lower is higher priority).</param>
/// <param name="Name">The display name.</param>
/// <param name="IsCutoff">Whether this tier is satisfactory. Items at this tier will not upgrade, but items above it continue upgrading.</param>
/// <param name="Criteria">The criteria that a release must match for this tier.</param>
public sealed record QualityTier(
    Guid Id,
    int Ordinal,
    string Name,
    bool IsCutoff,
    IReadOnlyList<QualityCriterion> Criteria);
