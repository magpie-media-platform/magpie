namespace Magpie.Models.Enums;

/// <summary>
/// The scope at which a job operates.
/// </summary>
public enum JobScope
{
    /// <summary>
    /// The scope for system-level jobs.
    /// </summary>
    System = 0,

    /// <summary>
    /// The scope for administrative jobs.
    /// </summary>
    Administrative,

    /// <summary>
    /// The scope for content jobs.
    /// </summary>
    Content,

    /// <summary>
    /// The scope for user jobs.
    /// </summary>
    User,
}
