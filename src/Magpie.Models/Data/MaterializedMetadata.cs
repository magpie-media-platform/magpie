using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// The merged metadata of a container, built from all provider contributions.
/// </summary>
public sealed record MaterializedMetadata
{
    /// <summary>
    /// The id of the container that holds the contributions.
    /// </summary>
    public Guid ContainerId { get; init; }

    /// <summary>
    /// The id of the content hierarchy item that the metadata describes, if it describes one.
    /// </summary>
    public Guid? ContentHierarchyItemId { get; init; }

    /// <summary>
    /// The id of the subject that the metadata describes, if it describes one.
    /// </summary>
    public Guid? SubjectId { get; init; }

    /// <summary>
    /// The main title to show.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The title in the original language.
    /// </summary>
    public string? OriginalTitle { get; init; }

    /// <summary>
    /// The title to use for sort order.
    /// </summary>
    public string? SortTitle { get; init; }

    /// <summary>
    /// The primary language, as a language code.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// The long-form description.
    /// </summary>
    public string? Overview { get; init; }

    /// <summary>
    /// The date of first publication.
    /// </summary>
    public DateTime? PublishDate { get; init; }

    /// <summary>
    /// The run time.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// The position within the parent.
    /// </summary>
    public int? SequenceNumber { get; init; }

    /// <summary>
    /// The production state, such as ended or in production.
    /// </summary>
    public string? ProductionStatus { get; init; }

    /// <summary>
    /// The date when the subject was formed.
    /// </summary>
    public DateTime? Established { get; init; }

    /// <summary>
    /// The date when the subject ended.
    /// </summary>
    public DateTime? Dissolved { get; init; }

    /// <summary>
    /// The date of birth.
    /// </summary>
    public DateTime? BirthDate { get; init; }

    /// <summary>
    /// The date of death.
    /// </summary>
    public DateTime? DeathDate { get; init; }

    /// <summary>
    /// The provider-defined values that no standard field holds.
    /// </summary>
    public IEnumerable<CustomMetadataValue>? CustomMetadata { get; init; }

    /// <summary>
    /// The ids at external providers.
    /// </summary>
    public IEnumerable<ExternalId>? ExternalIds { get; init; }

    /// <summary>
    /// The links to external pages.
    /// </summary>
    public IEnumerable<Reference>? References { get; init; }

    /// <summary>
    /// The links to other metadata containers.
    /// </summary>
    public IEnumerable<Relationship>? Relationships { get; init; }

    /// <summary>
    /// The artwork, such as posters and backdrops.
    /// </summary>
    public IEnumerable<Image>? Images { get; init; }

    /// <summary>
    /// The trailers and other preview clips.
    /// </summary>
    public IEnumerable<Preview>? Previews { get; init; }

    /// <summary>
    /// The titles and descriptions in other languages.
    /// </summary>
    public IEnumerable<AlternateText>? AlternateTexts { get; init; }

    /// <summary>
    /// The ratings from ratings bodies.
    /// </summary>
    public IEnumerable<ContentRating>? ContentRatings { get; init; }

    /// <summary>
    /// The awards and nominations.
    /// </summary>
    public IEnumerable<Award>? Awards { get; init; }

    /// <summary>
    /// The genres.
    /// </summary>
    public IEnumerable<Genre>? Genres { get; init; }
}
