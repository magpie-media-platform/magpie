using System;

namespace Magpie.Models.Api;

/// <summary>
/// One file's proposed destination or execution outcome.
/// </summary>
public sealed record FileRenameEntry
{
    /// <summary>
    /// The import candidate id when this is a pre-association preview.
    /// </summary>
    public Guid? CandidateId { get; init; }

    /// <summary>
    /// The associated media file id, when the file is already associated.
    /// </summary>
    public Guid? MediaFileId { get; init; }

    /// <summary>
    /// The content hierarchy item that owns the file.
    /// </summary>
    public Guid ContentHierarchyItemId { get; init; }

    /// <summary>
    /// The path before the operation.
    /// </summary>
    public required string OriginalPath { get; init; }

    /// <summary>
    /// The proposed path, or null when it cannot be resolved.
    /// </summary>
    public string? DestinationPath { get; init; }

    /// <summary>
    /// The failure that prevents this file's move.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Whether this file is a sidecar.
    /// </summary>
    public bool IsSidecar { get; init; }

    /// <summary>
    /// Whether execution completed for this file.
    /// </summary>
    public bool Completed { get; init; }
}
