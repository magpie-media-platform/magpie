using System;

using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for metadata
/// </summary>
public sealed record MetadataCriteria
{
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.ProviderType"/>
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> ProviderType { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Source"/>
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Source { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Title"/>
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Title { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.OriginalTitle"/>
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> OriginalTitle { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Language"/>
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Language { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Overview"/>
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Overview { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.PublishDate"/>
    /// </summary>
    public CriteriaSet<PropertyCriterion<DateTime>> PublishDate { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Duration"/>
    /// </summary>
    public CriteriaSet<PropertyCriterion<TimeSpan>> Duration { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.ProductionStatus"/>
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> ProductionStatus { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Established"/>
    /// </summary>
    public CriteriaSet<PropertyCriterion<DateTime>> Established { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Dissolved"/>
    /// </summary>
    public CriteriaSet<PropertyCriterion<DateTime>> Dissolved { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.BirthDate"/>
    /// </summary>
    public CriteriaSet<PropertyCriterion<DateTime>> BirthDate { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.DeathDate"/>
    /// </summary>
    public CriteriaSet<PropertyCriterion<DateTime>> DeathDate { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.CustomMetadata"/>
    /// </summary>
    public DictionaryCriteria<PropertyCriterion<string>> CustomMetadata { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Awards"/>
    /// </summary>
    public CollectionCriteria<AwardCriteria> Awards { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.ExternalIds"/>
    /// </summary>
    public CollectionCriteria<ExternalIdCriteria> ExternalIds { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.References"/>
    /// </summary>
    public CollectionCriteria<ReferenceMetadataCriteria> References { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Relationships"/>
    /// </summary>
    public CollectionCriteria<RelationshipCriteria> Relationships { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Images"/>
    /// </summary>
    public CollectionCriteria<ImageCriteria> Images { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Previews"/>
    /// </summary>
    public CollectionCriteria<PreviewMetadataCriteria> Previews { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.AlternateTexts"/>
    /// </summary>
    public CollectionCriteria<AlternateTextCriteria> AlternateTexts { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.ContentRatings"/>
    /// </summary>
    public CollectionCriteria<ContentRatingCriteria> ContentRatings { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Genres"/>.
    /// </summary>
    public CollectionCriteria<PropertyCriterion<string>> Genres { get; init; } = new();
    /// <summary>
    /// The criteria for associated <see cref="Tag"/>s.
    /// </summary>
    public CollectionCriteria<PropertyCriterion<string>> Tags { get; init; } = new();
}
