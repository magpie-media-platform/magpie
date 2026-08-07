using System.Collections.Generic;

namespace Magpie.Models.Import;

/// <summary>
/// The result of a request to queue library import candidates.
/// </summary>
public sealed record LibraryImportSubmissionResult
{
    /// <summary>
    /// The candidates that the request claimed and queued for import.
    /// </summary>
    public required IReadOnlyList<LibraryImportQueuedResult> Queued { get; init; }

    /// <summary>
    /// The candidates that the request skipped, each with its reason.
    /// </summary>
    public required IReadOnlyList<LibraryImportSkippedResult> Skipped { get; init; }
}
