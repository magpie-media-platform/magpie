namespace Magpie.Models;

/// <summary>
/// The result of a cancellation request for a job execution.
/// </summary>
/// <param name="OwnerNodeId">The id of the node that runs the execution, or null when no node owns it</param>
/// <param name="CancelledBeforeStart">Whether the execution was queued and went directly to cancelled</param>
public sealed record JobCancellationRequestResult(string? OwnerNodeId, bool CancelledBeforeStart);
