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
    public required string Slug { get; init; }

    /// <summary>
    /// The slug of the parent level, or null for a root level.
    /// </summary>
    public string? ParentSlug { get; init; }

    /// <summary>
    /// The display name of the level.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The name of the icon for the level.
    /// </summary>
    public string? IconName { get; init; }

    /// <summary>
    /// The standard metadata fields that this level supports.
    /// </summary>
    public MetadataField SupportedMetadataFields { get; init; } = MetadataField.All;

    /// <summary>
    /// The custom metadata fields of this level.
    /// </summary>
    public required IReadOnlyList<CustomMetadataDefinition> CustomMetadataFields { get; init; }

    /// <summary>
    /// The ordered standard facts that this level shows, with their labels and formats.
    /// </summary>
    public IReadOnlyList<CustomMetadataDefinition> StandardFactFields { get; init; } = [];

    /// <summary>
    /// Whether a node at this level can be the target of a release acquisition.
    /// </summary>
    public bool IsAcquirable { get; init; }

    /// <summary>
    /// The kind of ordinal that numbers the nodes at this level.
    /// </summary>
    public OrdinalKind OrdinalKind { get; init; } = OrdinalKind.None;

    /// <summary>
    /// The file extensions that this level accepts.
    /// </summary>
    public IReadOnlyList<string> AcceptedFileTypes { get; init; } = [];

    /// <summary>
    /// The sidecar file extensions that this level accepts.
    /// </summary>
    public IReadOnlyList<string> AcceptedSidecarTypes { get; init; } = [];

    /// <summary>
    /// The root subfolder, an empty string for a generated item path, or <see langword="null"/> for no path contribution.
    /// </summary>
    public string? Subfolder { get; init; }
}
