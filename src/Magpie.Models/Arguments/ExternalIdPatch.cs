using System;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for an external id.
/// </summary>
public sealed record ExternalIdPatch
{
    /// <summary>
    /// The id of the external id to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the position in the display order.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The patch for the external id value.
    /// </summary>
    public ScalarPatch<string>? Value { get; init; }

    /// <summary>
    /// The patch for the name of the source.
    /// </summary>
    public ScalarPatch<string?>? Source { get; init; }
}
