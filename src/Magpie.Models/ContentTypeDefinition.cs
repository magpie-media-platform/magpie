using System.Collections.Generic;

namespace Magpie.Models;

/// <summary>
/// The definition of a content type and its hierarchy.
/// </summary>
public sealed record ContentTypeDefinition
{
    /// <summary>
    /// The stable slug of the content type.
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// The display name of the content type.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The plural display name of the content type.
    /// </summary>
    public string? PluralDisplayName { get; init; }

    /// <summary>
    /// The name of the icon for the content type.
    /// </summary>
    public string? IconName { get; init; }

    /// <summary>
    /// The level definitions of the hierarchy, from root to leaf.
    /// </summary>
    public required IReadOnlyList<ContentHierarchyLevelDefinition> LevelDefinitions { get; init; }

    /// <summary>
    /// The category slugs of the content type.
    /// </summary>
    public IReadOnlyList<string> Categories { get; init; } = [];
}
