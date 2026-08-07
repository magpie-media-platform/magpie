using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models;

/// <summary>
/// A content item's metadata contribution together with the definitions of its immediate children.
/// </summary>
public sealed record ContentHierarchyMetadataResult
{
    /// <summary>
    /// The item's metadata contribution, or <c>null</c> when the provider has none.
    /// </summary>
    public MetadataContribution? Contribution { get; init; }

    /// <summary>
    /// The definitions of the item's immediate children.
    /// </summary>
    public IReadOnlyList<ChildHierarchyDefinition> Children { get; init; } = [];
}
