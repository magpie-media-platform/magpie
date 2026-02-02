namespace Magpie.Models.Enums;

/// <summary>
/// The status of a job execution.
/// </summary>
public enum JobExecutionStatus
{
    /// <summary>
    /// The job has been created, but is not ready to be queued or run.
    /// </summary>
    Created = 0,

    /// <summary>
    /// The job is queued and waiting to run.
    /// </summary>
    Queued,

    /// <summary>
    /// The job is currently running.
    /// </summary>
    Running,

    /// <summary>
    /// The job completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The job failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The job was cancelled.
    /// </summary>
    Cancelled,

    /// <summary>
    /// A cancellation has been requested but the job is still running.
    /// </summary>
    CancellationRequested
}
