using System;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a content rating.
/// </summary>
public sealed record ContentRatingPatch
{
    /// <summary>
    /// The id of the content rating to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the position in the display order.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The patch for the organization that issues the rating.
    /// </summary>
    public ScalarPatch<string>? Body { get; init; }

    /// <summary>
    /// The patch for the rating value.
    /// </summary>
    public ScalarPatch<string>? Rating { get; init; }
}
