namespace Magpie.Models.Enums;

/// <summary>
/// The way a job execution starts.
/// </summary>
public enum JobExecutionMode
{
    /// <summary>
    /// The job goes to the queue and runs at the discretion of the runtime.
    /// </summary>
    Queue = 0,

    /// <summary>
    /// The job runs at once.
    /// </summary>
    Immediate = 1
}
