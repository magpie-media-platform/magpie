namespace Magpie.Models.Jobs;

/// <summary>
/// The tree of job executions under one root execution.
/// </summary>
public sealed record JobExecutionTree
{
    /// <summary>
    /// The job execution at the root of the tree.
    /// </summary>
    public JobExecutionNode? Root { get; init; }

    /// <summary>
    /// The number of job executions in the tree.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Whether the tree reached the node limit and can omit job executions.
    /// </summary>
    public bool Truncated { get; init; }
}
