using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="QualityTier"/>.
/// </summary>
public sealed record QualityTierPatch
{
    /// <summary>
    /// The <see cref="QualityTier.Id"/> of the item to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The <see cref="QualityTier.Name"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The <see cref="QualityTier.Criteria"/> to patch.
    /// </summary>
    public QualityTierCriteriaPatch? Criteria { get; init; }
}
