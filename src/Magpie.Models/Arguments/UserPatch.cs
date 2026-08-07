using System;

using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a user.
/// </summary>
public sealed record UserPatch
{
    /// <summary>
    /// The id of the user to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for whether the user is enabled.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }

    /// <summary>
    /// The patch for the role of the user.
    /// </summary>
    public ScalarPatch<Role>? Role { get; init; }
}
