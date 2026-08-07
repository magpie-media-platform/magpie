using System;

namespace Magpie.Models.Import;

/// <summary>
/// One claimed library import candidate with its queued import job.
/// </summary>
public sealed record LibraryImportQueuedResult
{
    /// <summary>
    /// The id of the claimed candidate.
    /// </summary>
    public required Guid CandidateId { get; init; }

    /// <summary>
    /// The id of the import job execution on the queue.
    /// </summary>
    public required Guid JobExecutionId { get; init; }
}
