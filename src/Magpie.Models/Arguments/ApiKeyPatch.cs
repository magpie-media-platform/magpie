using System;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for an API key.
/// </summary>
public sealed record ApiKeyPatch
{
    /// <summary>
    /// The id of the API key to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for whether the key is enabled.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }
}
