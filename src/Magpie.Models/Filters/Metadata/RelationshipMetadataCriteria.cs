using System;

namespace Magpie.Models.Filters.Metadata;

/// <summary>
/// Filter criteria for <see cref="Models.Metadata.RelationshipMetadata"/>.
/// </summary>
public sealed record RelationshipMetadataCriteria
{
    /// <inheritdoc cref="Models.Metadata.RelationshipMetadata.RelatedMetadataId" />
    public AnyOfCriteria<FieldCriterion<Guid>> RelatedMetadataId { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.RelationshipMetadata.Ordinal" />
    public AnyOfCriteria<FieldCriterion<int>> Ordinal { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.RelationshipMetadata.Relation" />
    public AnyOfCriteria<FieldCriterion<string>> Relation { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.RelationshipMetadata.Qualifier" />
    public AnyOfCriteria<FieldCriterion<string>> Qualifier { get; init; } = new();
}
