using System;

namespace Magpie.Models.Import;

/// <summary>
/// The result of the import of one claimed library import candidate.
/// </summary>
public sealed record LibraryImportCompletionResult
{
    /// <summary>
    /// The status at the end of the import.
    /// </summary>
    public required LibraryImportCompletionStatus Status { get; init; }

    /// <summary>
    /// The message for a failed or skipped import, or <see langword="null"/> after a success.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// The id of the content hierarchy item that the import created.
    /// </summary>
    public Guid? ContentHierarchyItemId { get; init; }
}
