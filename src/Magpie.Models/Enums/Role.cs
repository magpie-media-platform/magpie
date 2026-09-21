namespace Magpie.Models.Enums;

/// <summary>
/// The access role of a user.
/// </summary>
public enum Role
{
    /// <summary>
    /// Read-only access.
    /// </summary>
    Viewer = 0,

    /// <summary>
    /// The right to request content.
    /// </summary>
    Requester = 1,

    /// <summary>
    /// The right to manage content.
    /// </summary>
    Manager = 2,

    /// <summary>
    /// Full administrative access.
    /// </summary>
    Admin = 3
}
