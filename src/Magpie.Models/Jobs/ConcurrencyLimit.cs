namespace Magpie.Models.Jobs;

/// <summary>
/// The limit on the executions of a job type that run at once.
/// </summary>
public sealed record ConcurrencyLimit
{
    /// <summary>
    /// The maximum number of executions that run at once.
    /// </summary>
    public int Max { get; set; }

    /// <summary>
    /// Whether each node counts only its own executions.
    /// </summary>
    public bool PerNode { get; set; }
}
