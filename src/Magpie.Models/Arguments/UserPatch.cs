using System;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for a user account.
/// </summary>
public record UserPatch
{
    /// <summary>
    /// The <see cref="User.Id"/> of the item to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The <see cref="User.Enabled"/> to patch.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }

    /// <summary>
    /// The <see cref="User.Role"/> to patch.
    /// </summary>
    public ScalarPatch<Role>? Role { get; init; }
}
