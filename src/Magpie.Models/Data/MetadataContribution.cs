using System;
using System.Collections.Generic;

using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// The metadata for one item from one provider.
/// </summary>
public record MetadataContribution
{
    /// <summary>
    /// The unique id of the metadata contribution.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the container that holds the contribution.
    /// </summary>
    public Guid MetadataContainerId { get; set; }

    /// <summary>
    /// The position in display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The id of the extension that supplies the provider.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The name of the provider type that made the contribution.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// The name of the upstream source of the data.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// The main title to show.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The title in the original language.
    /// </summary>
    public string? OriginalTitle { get; set; }

    /// <summary>
    /// The title to use for sort order.
    /// </summary>
    public string? SortTitle { get; set; }

    /// <summary>
    /// The primary language, as a language code.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// The long-form description.
    /// </summary>
    public string? Overview { get; set; }

    /// <summary>
    /// The date of first publication.
    /// </summary>
    public DateTime? PublishDate { get; set; }

    /// <summary>
    /// The calendar precision of <see cref="PublishDate"/>, or null when the provider gives no precision.
    /// </summary>
    public TemporalPrecision? PublishDatePrecision { get; set; }

    /// <summary>
    /// The run time.
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// The position within the parent.
    /// </summary>
    public int? SequenceNumber { get; set; }

    /// <summary>
    /// The production state, such as ended or in production.
    /// </summary>
    public string? ProductionStatus { get; set; }

    /// <summary>
    /// The date when the subject was formed.
    /// </summary>
    public DateTime? Established { get; set; }

    /// <summary>
    /// The date when the subject ended.
    /// </summary>
    public DateTime? Dissolved { get; set; }

    /// <summary>
    /// The date of birth.
    /// </summary>
    public DateTime? BirthDate { get; set; }

    /// <summary>
    /// The date of death.
    /// </summary>
    public DateTime? DeathDate { get; set; }

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time when the record last changed.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last changed the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }

    /// <summary>
    /// The UTC time of the last refresh from the provider.
    /// </summary>
    public DateTime? DateLastRefreshed { get; set; }

    /// <summary>
    /// The <see cref="MetadataContainer"/> that holds the contribution.
    /// </summary>
    public MetadataContainer? MetadataContainer { get; set; }

    /// <summary>
    /// The titles and descriptions in other languages.
    /// </summary>
    public ICollection<AlternateText> AlternateTexts { get; } = [];

    /// <summary>
    /// The awards and nominations.
    /// </summary>
    public ICollection<Award> Awards { get; } = [];

    /// <summary>
    /// The ratings from ratings bodies.
    /// </summary>
    public ICollection<ContentRating> ContentRatings { get; } = [];

    /// <summary>
    /// The provider-defined values that no standard field holds.
    /// </summary>
    public ICollection<CustomMetadataValue> CustomMetadata { get; } = [];

    /// <summary>
    /// The ids at external providers.
    /// </summary>
    public ICollection<ExternalId> ExternalIds { get; } = [];

    /// <summary>
    /// The genre classifications.
    /// </summary>
    public ICollection<Genre> Genres { get; } = [];

    /// <summary>
    /// The artwork, such as posters and backdrops.
    /// </summary>
    public ICollection<Image> Images { get; } = [];

    /// <summary>
    /// The trailers and other preview clips.
    /// </summary>
    public ICollection<Preview> Previews { get; } = [];

    /// <summary>
    /// The links to external pages.
    /// </summary>
    public ICollection<Reference> References { get; } = [];

    /// <summary>
    /// The links to other metadata containers.
    /// </summary>
    public ICollection<Relationship> Relationships { get; } = [];
}
