using System;

namespace Magpie.Models.Filters.Metadata;

/// <summary>
/// Filter criteria for <see cref="Models.Metadata.MetadataContribution"/>.
/// </summary>
public sealed record MetadataContributionCriteria
{
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.Provider" />
    public AnyOfCriteria<FieldCriterion<string>> Provider { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.Attribution" />
    public AnyOfCriteria<FieldCriterion<string>> Attribution { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.DateAdded" />
    public AnyOfCriteria<FieldCriterion<DateTimeOffset>> DateAdded { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.DateLastRefreshed" />
    public AnyOfCriteria<FieldCriterion<DateTimeOffset>> DateLastRefreshed { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.Title" />
    public AnyOfCriteria<FieldCriterion<string>> Title { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.OriginalTitle" />
    public AnyOfCriteria<FieldCriterion<string>> OriginalTitle { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.SortTitle" />
    public AnyOfCriteria<FieldCriterion<string>> SortTitle { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.Language" />
    public AnyOfCriteria<FieldCriterion<string>> Language { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.Overview" />
    public AnyOfCriteria<FieldCriterion<string>> Overview { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.Ordinal" />
    public AnyOfCriteria<FieldCriterion<int>> Ordinal { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.PublishDate" />
    public AnyOfCriteria<FieldCriterion<DateTime>> PublishDate { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.Duration" />
    public AnyOfCriteria<FieldCriterion<TimeSpan>> Duration { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.ProductionStatus" />
    public AnyOfCriteria<FieldCriterion<string>> ProductionStatus { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.Established" />
    public AnyOfCriteria<FieldCriterion<DateTime>> Established { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.Dissolved" />
    public AnyOfCriteria<FieldCriterion<DateTime>> Dissolved { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.BirthDate" />
    public AnyOfCriteria<FieldCriterion<DateTime>> BirthDate { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.DeathDate" />
    public AnyOfCriteria<FieldCriterion<DateTime>> DeathDate { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.CustomMetadata" />
    public DictionaryCriteria<AnyOfCriteria<FieldCriterion<string>>> CustomMetadata { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.ExternalIds" />
    public CollectionCriteria<ExternalIdMetadataCriteria> ExternalIds { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.References" />
    public CollectionCriteria<ReferenceMetadataCriteria> References { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.Relationships" />
    public CollectionCriteria<RelationshipMetadataCriteria> Relationships { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.Images" />
    public CollectionCriteria<ImageMetadataCriteria> Images { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.Previews" />
    public CollectionCriteria<PreviewMetadataCriteria> Previews { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.AlternateTexts" />
    public CollectionCriteria<AlternateTextMetadataCriteria> AlternateTexts { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.ContentRatings" />
    public CollectionCriteria<ContentRatingMetadataCriteria> ContentRatings { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.Awards" />
    public CollectionCriteria<AwardMetadataCriteria> Awards { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.MetadataContribution.Genres" />
    public CollectionCriteria<GenreMetadataCriteria> Genres { get; init; } = new();
}
