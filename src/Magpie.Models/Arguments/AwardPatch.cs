using System;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for an award.
/// </summary>
public sealed record AwardPatch
{
    /// <summary>
    /// The id of the award to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the position in the display order.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The patch for the name of the body that gives the award.
    /// </summary>
    public ScalarPatch<string>? Body { get; init; }

    /// <summary>
    /// The patch for the display name of the award.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for the category of the award.
    /// </summary>
    public ScalarPatch<string?>? Category { get; init; }

    /// <summary>
    /// The patch for the status of the award.
    /// </summary>
    public ScalarPatch<string?>? Status { get; init; }

    /// <summary>
    /// The patch for the year of the award.
    /// </summary>
    public ScalarPatch<int?>? Year { get; init; }

    /// <summary>
    /// The patch for the language code of the award.
    /// </summary>
    public ScalarPatch<string?>? Language { get; init; }

    /// <summary>
    /// The patch for the country code of the award.
    /// </summary>
    public ScalarPatch<string?>? Country { get; init; }
}
