using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// The definition of one level in a content type hierarchy.
/// </summary>
public sealed record ContentHierarchyLevelDefinition
{
    /// <summary>
    /// The canonical slug of the level.
    /// </summary>
    public required string Slug { get; set; }

    /// <summary>
    /// The slug of the parent level, or null for a root level.
    /// </summary>
    public string? ParentSlug { get; set; }

    /// <summary>
    /// The display name of the level.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// The name of the icon for the level.
    /// </summary>
    public string? IconName { get; set; }

    /// <summary>
    /// The standard metadata fields that this level supports.
    /// </summary>
    public MetadataField SupportedMetadataFields { get; set; } = MetadataField.All;

    /// <summary>
    /// The custom metadata fields of this level.
    /// </summary>
    public required IReadOnlyList<CustomMetadataDefinition> CustomMetadataFields { get; set; }

    /// <summary>
    /// The ordered standard facts that this level shows, with their labels and formats.
    /// </summary>
    public IReadOnlyList<CustomMetadataDefinition> StandardFactFields { get; set; } = [];

    /// <summary>
    /// Whether a node at this level can be the target of a release acquisition.
    /// </summary>
    public bool IsAcquirable { get; set; }

    /// <summary>
    /// The kind of ordinal that numbers the nodes at this level.
    /// </summary>
    public OrdinalKind OrdinalKind { get; set; } = OrdinalKind.None;

    /// <summary>
    /// The file extensions that this level accepts.
    /// </summary>
    public IReadOnlyList<string> AcceptedFileTypes { get; set; } = [];

    /// <summary>
    /// The sidecar file extensions that this level accepts.
    /// </summary>
    public IReadOnlyList<string> AcceptedSidecarTypes { get; set; } = [];

    /// <summary>
    /// The root subfolder, an empty string for a generated item path, or <see langword="null"/> for no path contribution.
    /// </summary>
    public string? Subfolder { get; set; }
}
