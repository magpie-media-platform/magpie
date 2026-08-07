using System.Collections.Generic;

using Magpie.Models.Enums;
namespace Magpie.Models.Security;

/// <summary>
/// The role of a caller with the grant state of every named permission for that role.
/// </summary>
public sealed record EffectivePermissions
{
    /// <summary>
    /// The role of the caller.
    /// </summary>
    public required Role Role { get; set; }

    /// <summary>
    /// The grant state of each named permission, keyed by permission name.
    /// </summary>
    public required IReadOnlyDictionary<string, PermissionGrant> Permissions { get; set; }
}
