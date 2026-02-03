using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Tag"/>s. 
/// </summary>
public record TagsPatch
{
    /// <summary>
    /// The id of the entity to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The <see cref="Tag"/>s to patch.
    /// </summary>
    public SetPatch<string, string>? Tags { get; init; }
}
