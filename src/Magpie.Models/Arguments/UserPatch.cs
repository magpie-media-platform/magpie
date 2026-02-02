using System;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for a user account.
/// </summary>
/// <param name="Id">The <see cref="User.Id"/> of the user to patch.</param>
/// <param name="Enabled">The <see cref="User.Enabled"/> patch.</param>" 
/// <param name="Role">The <see cref="User.Role"/> patch.</param>
public record UserPatch(
    Guid Id,
    ScalarPatch<bool>? Enabled = null,
    ScalarPatch<Role>? Role = null);
