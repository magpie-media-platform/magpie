using Magpie.Models.Enums;
namespace Magpie.Models.Security;

/// <summary>
/// The grant state of one named permission for a caller.
/// </summary>
public sealed record PermissionGrant
{
    /// <summary>
    /// The minimum role that the permission requires.
    /// </summary>
    public required Role RequiredRole { get; init; }

    /// <summary>
    /// Whether the role of the caller meets the requirement.
    /// </summary>
    public required bool Granted { get; init; }
}
