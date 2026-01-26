using Magpie.Models.Content;

namespace Magpie.Models.Metadata;

/// <summary>
/// A content hierarchy level with its materialized metadata.
/// </summary>
/// <param name="ContentHierarchyLevel">The content hierarchy level.</param>
/// <param name="MaterializedMetadata">The materialized metadata for the content hierarchy level.</param>
public sealed record ContentHierarchyLevelWithMetadata(
    ContentHierarchyLevel ContentHierarchyLevel,
    MaterializedMetadata MaterializedMetadata);
