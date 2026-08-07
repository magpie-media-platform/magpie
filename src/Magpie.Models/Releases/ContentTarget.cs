using System;
using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models.Releases;

/// <summary>
/// The content item that a release search targets, with its lineage and metadata.
/// </summary>
public sealed record ContentTarget
{
    /// <summary>
    /// The content hierarchy item that the search targets.
    /// </summary>
    public required ContentHierarchyItem Item { get; init; }

    /// <summary>
    /// The content hierarchy items from the target to the root.
    /// </summary>
    public required IReadOnlyList<ContentHierarchyItem> Lineage { get; init; }

    /// <summary>
    /// The materialized metadata of each lineage item, keyed by content hierarchy item id.
    /// </summary>
    public required IReadOnlyDictionary<Guid, MaterializedMetadata> Metadata { get; init; }
}
