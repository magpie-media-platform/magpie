using System;

namespace Magpie.Models.Import;

/// <summary>
/// One skipped library import candidate with the reason for the skip.
/// </summary>
public sealed record LibraryImportSkippedResult
{
    /// <summary>
    /// The id of the skipped candidate.
    /// </summary>
    public required Guid CandidateId { get; init; }

    /// <summary>
    /// The reason for the skip.
    /// </summary>
    public required string Reason { get; init; }
}
