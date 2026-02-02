using System.Collections.Generic;

namespace Magpie.Models;

/// <summary>
/// Defines a content hierarchical structure for a content type.
/// </summary>
public sealed record ContentTypeDefinition
{
    /// <summary>
    /// A identifier url-friendly string identifier.
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// The display name of the content type.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The definitions of hierarchical levels that define the content structure.
    /// </summary>
    public required IReadOnlyList<ContentHierarchyLevelDefinition> LevelDefinitions { get; init; }
}
