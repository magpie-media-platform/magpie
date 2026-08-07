namespace Magpie.Models.Enums;

/// <summary>
/// The status of a job execution.
/// </summary>
public enum JobExecutionStatus
{
    /// <summary>
    /// The job waits in the queue.
    /// </summary>
    Queued = 1,

    /// <summary>
    /// The job runs now.
    /// </summary>
    Running = 2,

    /// <summary>
    /// The job is complete and had no error.
    /// </summary>
    Completed = 3,

    /// <summary>
    /// The job failed.
    /// </summary>
    Failed = 4,

    /// <summary>
    /// The job was cancelled.
    /// </summary>
    Cancelled = 5,

    /// <summary>
    /// A cancel request exists and the job still runs.
    /// </summary>
    CancellationRequested = 6
}
