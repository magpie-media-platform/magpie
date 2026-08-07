using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models;

/// <summary>
/// A request for the subject relationships of a content hierarchy item.
/// </summary>
public sealed record MetadataRelationshipRequest
{
    /// <summary>
    /// The content hierarchy item whose relationships are requested.
    /// </summary>
    public required ContentHierarchyItem Item { get; init; }

    /// <summary>
    /// The metadata contribution that names the related subjects.
    /// </summary>
    public required MetadataContribution Contribution { get; init; }

    /// <summary>
    /// The metadata that the item already holds.
    /// </summary>
    public required IReadOnlyList<MetadataContribution> ExistingMetadata { get; init; }

    /// <summary>
    /// The metadata contributions of the ancestors of the item.
    /// </summary>
    public IReadOnlyList<MetadataContribution> AncestorMetadata { get; init; } = [];
}
