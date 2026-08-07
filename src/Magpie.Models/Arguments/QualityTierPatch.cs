using System;
using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a quality tier.
/// </summary>
public sealed record QualityTierPatch
{
    /// <summary>
    /// The id of the quality tier to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the display name of the tier.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for the match criteria of the tier.
    /// </summary>
    public ScalarPatch<QualityTierCriteria>? Criteria { get; init; }

    /// <summary>
    /// The patch for the accept and exclude rules of the tier.
    /// </summary>
    public ScalarPatch<List<QualityTierRule>>? Rules { get; init; }
}
