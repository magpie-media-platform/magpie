namespace Magpie.Models.Import;

/// <summary>
/// The result of the import of a claimed library import candidate.
/// </summary>
public enum LibraryImportCompletionStatus
{
    /// <summary>
    /// The import finished and the item is in the library.
    /// </summary>
    Completed,

    /// <summary>
    /// The import failed and the candidate is in the failed status.
    /// </summary>
    Failed,

    /// <summary>
    /// The candidate is not in an importable status, so nothing changed.
    /// </summary>
    Skipped,

    /// <summary>
    /// The target level does not exist yet, so the candidate stays in the importing status for a later attempt.
    /// </summary>
    Waiting
}
