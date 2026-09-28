namespace Magpie.Models;

/// <summary>
/// The cancellation of a job execution.
/// </summary>
/// <param name="OwnerNodeId">The id of the node that runs the job execution, or null when no node owns it</param>
/// <param name="CancelledBeforeStart">Whether the job execution was queued and went directly to cancelled</param>
public sealed record JobCancellation(string? OwnerNodeId, bool CancelledBeforeStart);
