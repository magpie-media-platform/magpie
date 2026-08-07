using System;

using Magpie.Models.Data;
using Magpie.Models.Filtering;

/// <summary>
/// The filter criteria for a <see cref="Relationship"/>.
/// </summary>
public sealed record RelationshipCriteria
{
    /// <summary>
    /// The criteria on the external ids of the related item.
    /// </summary>
    public CollectionCriteria<ExternalIdCriteria> ExternalIds { get; init; } = new();
    /// <summary>
    /// The criteria on the id of the container at the other end of the link.
    /// </summary>
    public CriteriaSet<PropertyCriterion<Guid>> RelatedMetadataContainerId { get; init; } = new();
    /// <summary>
    /// The criteria on the kind of link.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Kind { get; init; } = new();
    /// <summary>
    /// The criteria on the type of the related item.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Type { get; init; } = new();
    /// <summary>
    /// The criteria on the extra detail for the kind.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Qualifier { get; init; } = new();
    /// <summary>
    /// The criteria on the name of the related subject.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Subject { get; init; } = new();
}
