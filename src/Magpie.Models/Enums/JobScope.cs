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
    Administrative = 1,

    /// <summary>
    /// The scope for content jobs.
    /// </summary>
    Content = 2,

    /// <summary>
    /// The scope for user jobs.
    /// </summary>
    User = 3,
}
