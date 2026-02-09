using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="ContentRating"/>.
/// </summary>
public sealed record ContentRatingPatch
{
    /// <summary>
    /// The <see cref="ContentRating.Id"/> of the item to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The <see cref="ContentRating.Ordinal"/> to patch.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The <see cref="ContentRating.Body"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Body { get; init; }

    /// <summary>
    /// The <see cref="ContentRating.Rating"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Rating { get; init; }
}
