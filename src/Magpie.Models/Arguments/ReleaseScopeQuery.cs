using System;
using System.Collections.Generic;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for release scope rows.
/// </summary>
public sealed record ReleaseScopeQuery
{
    /// <summary>
    /// The release id to match.
    /// </summary>
    public Guid? ReleaseId { get; init; }

    /// <summary>
    /// The release ids to match.
    /// </summary>
    public IReadOnlyCollection<Guid>? ReleaseIds { get; init; }

    /// <summary>
    /// The content hierarchy item id to match.
    /// </summary>
    public Guid? ContentHierarchyItemId { get; init; }
}
