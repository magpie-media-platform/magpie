using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Relationship"/>.
/// </summary>
public sealed record RelationshipPatch
{
    /// <summary>
    /// The <see cref="Relationship.Id"/> of the item to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The <see cref="Relationship.RelatedMetadataContainerId"/> to patch.
    /// </summary>
    public ScalarPatch<Guid>? RelatedMetadataContainerId { get; init; }

    /// <summary>
    /// The <see cref="Relationship.Ordinal"/> to patch.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The <see cref="Relationship.Kind"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Kind { get; init; }

    /// <summary>
    /// The <see cref="Relationship.Qualifier"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? Qualifier { get; init; }
}
