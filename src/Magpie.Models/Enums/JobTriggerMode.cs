namespace Magpie.Models.Enums;

/// <summary>
/// The trigger that can start a job definition.
/// </summary>
public enum JobTriggerMode
{
    /// <summary>
    /// A user or an API caller can start the job directly.
    /// </summary>
    Manual = 0,

    /// <summary>
    /// A schedule trigger can start the job.
    /// </summary>
    Schedule = 1,

    /// <summary>
    /// An event trigger can start the job.
    /// </summary>
    Event = 2
}
