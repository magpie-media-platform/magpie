using Magpie.Models.Data;

namespace Magpie.Models.Enums;

/// <summary>
/// The lifecycle status of a <see cref="LibraryImportCandidate"/> row.
/// </summary>
public enum LibraryImportCandidateStatus
{
    /// <summary>
    /// The row waits for operator review.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The scan failed on this file.
    /// </summary>
    Failed = 5,

    /// <summary>
    /// The file of the row is no longer under a configured root folder, so the pending decision is stale.
    /// </summary>
    Invalidated = 6,

    /// <summary>
    /// The row is claimed for import and a durable import job waits in the queue.
    /// </summary>
    Queued = 7,

    /// <summary>
    /// The import job hydrates the content and links the file.
    /// </summary>
    Importing = 8,

    /// <summary>
    /// The scan wrote the row and the match resolution job has not resolved it yet.
    /// </summary>
    MatchingPending = 9,

    /// <summary>
    /// The user rejected the row, so rescans keep it unmatched until the user restores it.
    /// </summary>
    Rejected = 10
}
