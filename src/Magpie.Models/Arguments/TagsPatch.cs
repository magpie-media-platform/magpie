using System;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The base partial-update arguments for tag edits.
/// </summary>
public record TagsPatch
{
    /// <summary>
    /// The id of the entity to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the tags.
    /// </summary>
    public SetPatch<string, string>? Tags { get; init; }
}
