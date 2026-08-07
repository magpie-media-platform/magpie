using System;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for download scope rows.
/// </summary>
public sealed record DownloadScopeQuery
{
    /// <summary>
    /// The download id to match.
    /// </summary>
    public Guid? DownloadId { get; init; }

    /// <summary>
    /// The content hierarchy item id to match.
    /// </summary>
    public Guid? ContentHierarchyItemId { get; init; }
}
