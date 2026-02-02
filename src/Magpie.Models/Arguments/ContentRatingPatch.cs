using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="ContentRating"/>.
/// </summary>
/// <param name="Id">The <see cref="ContentRating.Id"/> of the item to patch.</param>
/// <param name="Ordinal">The <see cref="ContentRating.Ordinal"/> patch</param>
/// <param name="RatingsBody">The <see cref="ContentRating.RatingsBody"/> patch</param>
/// <param name="Value">The <see cref="ContentRating.Value"/> patch</param>
public sealed record ContentRatingPatch(
    Guid Id,
    ScalarPatch<int>? Ordinal = null,
    ScalarPatch<string>? RatingsBody = null,
    ScalarPatch<string>? Value = null);
