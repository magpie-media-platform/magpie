namespace Magpie.Models.Enums;

/// <summary>
/// The scope at which a job operates.
/// </summary>
public enum JobScope
{
    /// <summary>
    /// A job that is scoped system operations.
    /// </summary>
    /// <remarks>
    /// System-scoped jobs cannot be triggered or modified by users. System administrators have read-only
    /// access to these jobs.
    /// </remarks>
    System = 0,

    /// <summary>
    /// A job scoped to administrative tasks.
    /// </summary> 
    /// <remarks>
    /// System administrators are permitted to configure triggers or manually run jobs of this scope,
    /// but they will be hidden from regular users.
    /// </remarks>
    Administrative,

    /// <summary>
    /// A job scoped to content management.
    /// </summary>
    /// <remarks>
    /// Content managers and system administrators are permitted to configure triggers
    /// or manually run jobs of this scope, but they will be hidden from view-only users.
    /// </remarks>
    Content,

    /// <summary>
    /// A job scoped to a specific user.
    /// </summary>
    /// <remarks>
    /// All users are permitted to configure triggers or manually run jobs of this scope. 
    /// Edit permissions will be locked to the owner or system administrators, and will be hidden from all other users.
    /// </remarks>
    User,
}
