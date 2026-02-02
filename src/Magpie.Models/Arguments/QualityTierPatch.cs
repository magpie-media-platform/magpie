using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="QualityTier"/>.
/// </summary>
/// <param name="Id">The <see cref="QualityTier.Id"/> of the item to patch.</param>
/// <param name="Name">The <see cref="QualityTier.Name"/> patch</param>
/// <param name="Criteria">The <see cref="QualityTier.Criteria"/> patch</param>
public sealed record QualityTierPatch(
    Guid Id,
    ScalarPatch<string>? Name = null,
    QualityTierCriteriaPatch? Criteria = null);
