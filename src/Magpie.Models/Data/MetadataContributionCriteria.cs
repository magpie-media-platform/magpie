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
    public AllCriteria<PropertyCriterion<string>> ProviderType { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Source"/>
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Source { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Title"/>
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Title { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.OriginalTitle"/>
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> OriginalTitle { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Language"/>
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Language { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Overview"/>
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Overview { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.PublishDate"/>
    /// </summary>
    public AllCriteria<PropertyCriterion<DateTime>> PublishDate { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Duration"/>
    /// </summary>
    public AllCriteria<PropertyCriterion<TimeSpan>> Duration { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.ProductionStatus"/>
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> ProductionStatus { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Established"/>
    /// </summary>
    public AllCriteria<PropertyCriterion<DateTime>> Established { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Dissolved"/>
    /// </summary>
    public AllCriteria<PropertyCriterion<DateTime>> Dissolved { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.BirthDate"/>
    /// </summary>
    public AllCriteria<PropertyCriterion<DateTime>> BirthDate { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.DeathDate"/>
    /// </summary>
    public AllCriteria<PropertyCriterion<DateTime>> DeathDate { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.CustomMetadata"/>
    /// </summary>
    public DictionaryCriteria<AllCriteria<PropertyCriterion<string>>> CustomMetadata { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Awards"/>
    /// </summary>
    public CollectionCriteria<AllCriteria<AwardCriteria>> Awards { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.ExternalIds"/>
    /// </summary>
    public CollectionCriteria<AllCriteria<ExternalIdCriteria>> ExternalIds { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.References"/>
    /// </summary>
    public CollectionCriteria<AllCriteria<ReferenceMetadataCriteria>> References { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Relationships"/>
    /// </summary>
    public CollectionCriteria<AllCriteria<RelationshipCriteria>> Relationships { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Images"/>
    /// </summary>
    public CollectionCriteria<AllCriteria<ImageCriteria>> Images { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Previews"/>
    /// </summary>
    public CollectionCriteria<AllCriteria<PreviewMetadataCriteria>> Previews { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.AlternateTexts"/>
    /// </summary>
    public CollectionCriteria<AllCriteria<AlternateTextCriteria>> AlternateTexts { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.ContentRatings"/>
    /// </summary>
    public CollectionCriteria<AllCriteria<ContentRatingCriteria>> ContentRatings { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MetadataContribution.Genres"/>.
    /// </summary>
    public CollectionCriteria<AllCriteria<PropertyCriterion<string>>> Genres { get; init; } = new();
    /// <summary>
    /// The criteria for associated <see cref="Tag"/>s.
    /// </summary>
    public CollectionCriteria<AllCriteria<PropertyCriterion<string>>> Tags { get; init; } = new();
}
