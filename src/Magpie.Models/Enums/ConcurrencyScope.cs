namespace Magpie.Models.Enums;

/// <summary>
/// The level of serialization between the executions of a job.
/// </summary>
public enum ConcurrencyScope
{
    /// <summary>
    /// Executions do not wait for each other.
    /// </summary>
    Parallel = 0,

    /// <summary>
    /// One execution per job type at a time, for all targets.
    /// </summary>
    PerType,

    /// <summary>
    /// One execution per job type and target at a time.
    /// </summary>
    PerTypeAndTarget,

    /// <summary>
    /// One execution per target at a time, for all job types.
    /// </summary>
    PerTarget,

    /// <summary>
    /// One execution at a time, for all job types and all targets.
    /// </summary>
    Global,
}
