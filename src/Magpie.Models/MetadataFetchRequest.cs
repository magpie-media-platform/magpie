using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models;

/// <summary>
/// A request for enrichment metadata about a content hierarchy item.
/// </summary>
public sealed record MetadataFetchRequest
{
    /// <summary>
    /// The content hierarchy item that needs metadata.
    /// </summary>
    public required ContentHierarchyItem Item { get; init; }

    /// <summary>
    /// The metadata that the item already holds.
    /// </summary>
    public required IReadOnlyList<MetadataContribution> ExistingMetadata { get; init; }

    /// <summary>
    /// The metadata contributions of the ancestors of the item.
    /// </summary>
    public IReadOnlyList<MetadataContribution> AncestorMetadata { get; init; } = [];

    /// <summary>
    /// The language preferences of the nearest ancestor.
    /// </summary>
    public IReadOnlyList<string> AncestorLanguagePreferences { get; init; } = [];

    /// <summary>
    /// The default language preference for Magpie.
    /// </summary>
    public required string DefaultLanguagePreference { get; init; }
}
