namespace Magpie.Models.Enums;

/// <summary>
/// Specifies how job executions are created.
/// </summary>
public enum JobExecutionMode
{
    /// <summary>
    /// The job is queued for execution and will run at the runtimes discretion.
    /// </summary>
    Queue,

    /// <summary>
    /// The job will be executed immediately.
    /// </summary>
    Immediate
}
