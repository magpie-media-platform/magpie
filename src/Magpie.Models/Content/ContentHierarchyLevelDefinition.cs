using System;
using System.Collections.Generic;

using Magpie.Models.Metadata;

namespace Magpie.Models.Content;

/// <summary>
/// Defines a single level within a content type hierarchy.
/// </summary>
/// <remarks>
/// <see cref="ContentTypeDefinition"/> defines content types and their hierarchical structure
/// (e.g., Series > Season > Episode). Instances form trees via ParentId references. Fields like
/// Ordinal are interpreted contextually: as episode number on Episode levels, season number on
/// Season levels, etc.
/// </remarks>
public sealed record ContentHierarchyLevelDefinition
{
    /// <summary>
    /// The unique identifier of the level definition.
    /// </summary>
    public Guid Id { get; init; }
    /// <summary>
    /// The parent level definition, or <c>null</c> if it is a root level.
    /// </summary>
    public Guid? ParentLevelId { get; init; }
    /// <summary>
    /// The name of this level.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// The metadata fields supported by this level.
    /// </summary>
    public MetadataField SupportedMetadataFields { get; init; } = MetadataField.All;
    /// <summary>
    /// The custom metadata fields defined for this level.
    /// </summary>
    public required IReadOnlyList<CustomMetadataFieldDefinition> CustomMetadataFields { get; init; }
    /// <summary>
    /// Whether instances of this level contribute a path segment.
    /// </summary>
    public bool IsPathProvider { get; init; } = true;
}
