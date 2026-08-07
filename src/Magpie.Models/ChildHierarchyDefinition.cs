using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models;

/// <summary>
/// The definition of a child hierarchy item from provider metadata.
/// </summary>
public sealed record ChildHierarchyDefinition
{
    /// <summary>
    /// The hierarchy level slug of the child item.
    /// </summary>
    public required string Level { get; init; }

    /// <summary>
    /// The display name of the child item.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The stable slug of the child item.
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// The external identifiers of the child item, keyed by source.
    /// </summary>
    public IReadOnlyDictionary<string, string> ExternalIds { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The first metadata contribution for the child item, when any.
    /// </summary>
    public MetadataContribution? InitialContribution { get; init; }

    /// <summary>
    /// The child items below this item.
    /// </summary>
    public IReadOnlyList<ChildHierarchyDefinition> Children { get; init; } = [];

    /// <summary>
    /// The position of the child item among its siblings, when any.
    /// </summary>
    public int? Ordinal { get; init; }

    /// <summary>
    /// The key that aligns this definition with an existing child item.
    /// </summary>
    public string? AlignmentKey { get; init; }
}
