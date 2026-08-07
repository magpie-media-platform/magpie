namespace Magpie.Models.Jobs;

/// <summary>
/// The policy for a failed job execution.
/// </summary>
public enum JobFailurePolicy
{
    /// <summary>
    /// The execution stops in the failed status.
    /// </summary>
    Fail = 0,

    /// <summary>
    /// The execution runs again.
    /// </summary>
    Retry,

    /// <summary>
    /// The execution goes back on the queue under the bounded retry policy.
    /// </summary>
    Requeue
}
