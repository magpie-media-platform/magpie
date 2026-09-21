using System;
using System.Collections.Generic;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query criteria for media files.
/// </summary>
public sealed record MediaFileQuery
{
    /// <summary>
    /// The content item ids to match, or <c>null</c> for all ids.
    /// </summary>
    public IReadOnlyCollection<Guid>? ContentItemIds { get; init; }

    /// <summary>
    /// The content hierarchy item id to match, or <c>null</c> for all items.
    /// </summary>
    public Guid? ContentHierarchyItemId { get; init; }

    /// <summary>
    /// The file paths to match, or <c>null</c> for all paths.
    /// </summary>
    public IReadOnlyCollection<string>? FilePaths { get; init; }

    /// <summary>
    /// The directory that the file paths must be under, or <c>null</c> for all paths.
    /// </summary>
    public string? FilePathPrefix { get; init; }

    /// <summary>
    /// The UTC cutoff for missing or stale media info, or <c>null</c> for all files.
    /// </summary>
    public DateTime? MediaInfoStaleBefore { get; init; }
}
