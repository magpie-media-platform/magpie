using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// Defines a single level within a content type hierarchy.
/// </summary>
public sealed record ContentHierarchyLevelDefinition
{
    /// <summary>
    /// A url-save identifier of the level definition that is unique within the content type.
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// The parent level's slug, or null if this is a root level.
    /// </summary>
    public string? ParentSlug { get; init; }

    /// <summary>
    /// The display name of this level.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The metadata fields supported by this level.
    /// </summary>
    public MetadataField SupportedMetadataFields { get; init; } = MetadataField.All;

    /// <summary>
    /// The custom metadata fields defined for this level.
    /// </summary>
    public required IReadOnlyList<CustomMetadataDefinition> CustomMetadataFields { get; init; }
}
