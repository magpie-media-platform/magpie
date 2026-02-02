using System;

using Magpie.Models.Data;
using Magpie.Models.Filtering;

/// <summary>
/// Filter criteria for <see cref="Relationship"/>.
/// </summary>
public sealed record RelationshipCriteria
{
    /// <inheritdoc cref="Relationship.RelatedMetadataContainerId" />
    public AllCriteria<PropertyCriterion<Guid>> RelatedMetadataContainerId { get; init; } = new();
    /// <inheritdoc cref="Relationship.Kind" />
    public AllCriteria<PropertyCriterion<string>> Kind { get; init; } = new();
    /// <inheritdoc cref="Relationship.Qualifier" />
    public AllCriteria<PropertyCriterion<string>> Qualifier { get; init; } = new();
}
