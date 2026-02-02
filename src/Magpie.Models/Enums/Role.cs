namespace Magpie.Models.Enums;

/// <summary>
/// User roles.
/// </summary>
public enum Role
{
    /// <summary>
    /// Allows read-only access.
    /// </summary>
    Viewer = 0,

    /// <summary>
    /// Allows requesting content.
    /// </summary>
    Requester,

    /// <summary>
    /// Allows managing content.
    /// </summary>
    Manager,

    /// <summary>
    /// Allows full administrative access.
    /// </summary>
    Admin
}
