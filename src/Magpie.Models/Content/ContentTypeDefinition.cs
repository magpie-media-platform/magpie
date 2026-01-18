using System;
using System.Collections.Generic;

namespace Magpie.Models.Content;

/// <summary>
/// Defines a content hierarchical structure for a content type.
/// </summary>
public sealed record ContentTypeDefinition
{
    /// <summary>
    /// The unique identifier of the content type definition.
    /// </summary>
    public Guid Id { get; init; }
    /// <summary>
    /// The version of the content type definition.
    /// </summary>
    public int? Version { get; init; }
    /// <summary>
    /// The display name of the content type.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// The definitions of hierarchical levels that define the content structure.
    /// </summary>
    public required IReadOnlyList<ContentHierarchyLevelDefinition> LevelDefinitions { get; init; }
}
