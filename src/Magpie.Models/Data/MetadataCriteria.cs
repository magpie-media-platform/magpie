using System;
using System.Collections.Generic;

using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// The filter criteria for a <see cref="MetadataContribution"/>.
/// </summary>
public sealed record MetadataCriteria
{
    /// <summary>
    /// The metadata container ids to include.
    /// </summary>
    public IReadOnlyCollection<Guid> ContainerIds { get; init; } = [];

    /// <summary>
    /// The criteria on the name of the provider type.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> ProviderType { get; init; } = new();

    /// <summary>
    /// The criteria on the name of the upstream source.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Source { get; init; } = new();

    /// <summary>
    /// The criteria on the main title.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Title { get; init; } = new();

    /// <summary>
    /// The criteria on the title in the original language.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> OriginalTitle { get; init; } = new();

    /// <summary>
    /// The criteria on the primary language code.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Language { get; init; } = new();

    /// <summary>
    /// The criteria on the long-form description.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Overview { get; init; } = new();

    /// <summary>
    /// The criteria on the date of first publication.
    /// </summary>
    public CriteriaSet<PropertyCriterion<TemporalValue>> PublishDate { get; init; } = new();

    /// <summary>
    /// The criteria on the run time.
    /// </summary>
    public CriteriaSet<PropertyCriterion<TimeSpan>> Duration { get; init; } = new();

    /// <summary>
    /// The criteria on the production state.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> ProductionStatus { get; init; } = new();

    /// <summary>
    /// The criteria on the date when the subject was formed.
    /// </summary>
    public CriteriaSet<PropertyCriterion<TemporalValue>> Established { get; init; } = new();

    /// <summary>
    /// The criteria on the date when the subject ended.
    /// </summary>
    public CriteriaSet<PropertyCriterion<TemporalValue>> Dissolved { get; init; } = new();

    /// <summary>
    /// The criteria on the date of birth.
    /// </summary>
    public CriteriaSet<PropertyCriterion<TemporalValue>> BirthDate { get; init; } = new();

    /// <summary>
    /// The criteria on the date of death.
    /// </summary>
    public CriteriaSet<PropertyCriterion<TemporalValue>> DeathDate { get; init; } = new();

    /// <summary>
    /// The criteria on the provider-defined values, keyed by name.
    /// </summary>
    public CriteriaSet<DictionaryCriteria<PropertyCriterion<string>>> CustomMetadata { get; init; } = new();

    /// <summary>
    /// The criteria on the awards and nominations.
    /// </summary>
    public CollectionCriteria<AwardCriteria> Awards { get; init; } = new();

    /// <summary>
    /// The criteria on the ids at external providers.
    /// </summary>
    public CollectionCriteria<ExternalIdCriteria> ExternalIds { get; init; } = new();

    /// <summary>
    /// The criteria on the links to external pages.
    /// </summary>
    public CollectionCriteria<ReferenceMetadataCriteria> References { get; init; } = new();

    /// <summary>
    /// The criteria on the links to other metadata containers.
    /// </summary>
    public CollectionCriteria<RelationshipCriteria> Relationships { get; init; } = new();

    /// <summary>
    /// The criteria on the artwork.
    /// </summary>
    public CollectionCriteria<ImageCriteria> Images { get; init; } = new();

    /// <summary>
    /// The criteria on the trailers and other preview clips.
    /// </summary>
    public CollectionCriteria<PreviewMetadataCriteria> Previews { get; init; } = new();

    /// <summary>
    /// The criteria on the titles and descriptions in other languages.
    /// </summary>
    public CollectionCriteria<AlternateTextCriteria> AlternateTexts { get; init; } = new();

    /// <summary>
    /// The criteria on the ratings from ratings bodies.
    /// </summary>
    public CollectionCriteria<ContentRatingCriteria> ContentRatings { get; init; } = new();

    /// <summary>
    /// The criteria on the genres.
    /// </summary>
    public CollectionCriteria<GenreCriteria> Genres { get; init; } = new();

    /// <summary>
    /// The criteria on the tags.
    /// </summary>
    public CollectionCriteria<TagCriteria> Tags { get; init; } = new();

    /// <summary>
    /// The language code to prefer for localized fields, or <c>null</c> for the default.
    /// </summary>
    public string? PreferredLanguage { get; init; }
}
