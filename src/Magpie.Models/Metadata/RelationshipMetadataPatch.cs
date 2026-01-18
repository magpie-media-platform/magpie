using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// Patch for <see cref="RelationshipMetadata"/>.
/// </summary>
public sealed record RelationshipMetadataPatch
{
    /// <inheritdoc cref="RelationshipMetadata.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="RelationshipMetadata.RelatedMetadataId" />
    public FieldPatch<Guid>? RelatedMetadataId { get; init; }
    /// <inheritdoc cref="RelationshipMetadata.Ordinal" />
    public FieldPatch<int>? Ordinal { get; init; }
    /// <inheritdoc cref="RelationshipMetadata.Relation" />
    public FieldPatch<string>? Relation { get; init; }
    /// <inheritdoc cref="RelationshipMetadata.Qualifier" />
    public FieldPatch<string?>? Qualifier { get; init; }
}
