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
    public required string Slug { get; set; }

    /// <summary>
    /// The display name of the content type.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// The plural display name of the content type.
    /// </summary>
    public string? PluralDisplayName { get; set; }

    /// <summary>
    /// The name of the icon for the content type.
    /// </summary>
    public string? IconName { get; set; }

    /// <summary>
    /// The level definitions of the hierarchy, from root to leaf.
    /// </summary>
    public required IReadOnlyList<ContentHierarchyLevelDefinition> LevelDefinitions { get; set; }

    /// <summary>
    /// The category slugs of the content type.
    /// </summary>
    public IReadOnlyList<string> Categories { get; set; } = [];
}
