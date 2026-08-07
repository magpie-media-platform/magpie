using System;
using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// The preview of a content item built from external metadata.
/// </summary>
public sealed record ContentPreview
{
    /// <summary>
    /// The provider type of the metadata source.
    /// </summary>
    public required string ProviderType { get; init; }

    /// <summary>
    /// The slug of the content type.
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// The slug of the content hierarchy level.
    /// </summary>
    public required string LevelSlug { get; init; }

    /// <summary>
    /// The title of the content.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The title of the content in the original language.
    /// </summary>
    public string? OriginalTitle { get; init; }

    /// <summary>
    /// The overview text for the content.
    /// </summary>
    public string? Overview { get; init; }

    /// <summary>
    /// The release date of the content.
    /// </summary>
    public DateTime? ReleaseDate { get; init; }

    /// <summary>
    /// The release year of the content.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// The duration of the content.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// The code of the primary language of the content.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// The production status of the content.
    /// </summary>
    public string? ProductionStatus { get; init; }

    /// <summary>
    /// The genres of the content.
    /// </summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>
    /// The content ratings from each rating body.
    /// </summary>
    public IReadOnlyList<ContentPreviewRating> ContentRatings { get; init; } = [];

    /// <summary>
    /// The images for the content.
    /// </summary>
    public IReadOnlyList<ContentPreviewImage> Images { get; init; } = [];

    /// <summary>
    /// The preview media for the content.
    /// </summary>
    public IReadOnlyList<ContentPreviewPreview> Previews { get; init; } = [];

    /// <summary>
    /// The external ids for the content.
    /// </summary>
    public IReadOnlyList<ContentPreviewExternalId> ExternalIds { get; init; } = [];

    /// <summary>
    /// The subjects related to the content.
    /// </summary>
    public IReadOnlyList<ContentPreviewRelatedSubject> RelatedSubjects { get; init; } = [];
}
