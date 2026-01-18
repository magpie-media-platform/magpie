namespace Magpie.Models.Users;

/// <summary>
/// User roles.
/// </summary>
public enum Role
{
    /// <summary>
    /// Read-only access. Can browse and view content.
    /// </summary>
    Viewer = 0,

    /// <summary>
    /// Can request content via wishlists and searches.
    /// </summary>
    Requester,

    /// <summary>
    /// Can manage content, metadata, and library organization.
    /// </summary>
    Manager,

    /// <summary>
    /// Full server administration access.
    /// </summary>
    Admin
}
