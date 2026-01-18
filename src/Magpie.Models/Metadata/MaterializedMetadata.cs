using System;
using System.Collections.Generic;

namespace Magpie.Models.Metadata;

/// <summary>
/// Aggregated metadata materialized from provider contributions.
/// </summary>
/// <param name="Title">The primary display title or name.</param>
/// <param name="OriginalTitle">The original title for localized content.</param>
/// <param name="SortTitle">The title or name used for sorting.</param>
/// <param name="Language">The primary language of the content.</param>
/// <param name="Overview">The primary synopsis, description, or biography.</param>
/// <param name="PublishDate">The original publication or release date.</param>
/// <param name="Duration">The runtime duration for media content.</param>
/// <param name="ProductionStatus">The production status.</param>
/// <param name="Established">The establishment date for organizations or groups.</param>
/// <param name="Dissolved">The dissolution date for organizations or groups.</param>
/// <param name="BirthDate">The birth date for people.</param>
/// <param name="DeathDate">The death date for people.</param>
/// <param name="CustomMetadata">Additional custom metadata as key-value pairs.</param>
/// <param name="ExternalIds">External identifiers from all providers.</param>
/// <param name="References">External references and informational links.</param>
/// <param name="Relationships">Relationships to subjects.</param>
/// <param name="Images">Images associated with this item.</param>
/// <param name="Previews">Previews associated with this item.</param>
/// <param name="AlternateTexts">Alternate text entries.</param>
/// <param name="ContentRatings">Content ratings from various bodies.</param>
/// <param name="Awards">Award nominations and wins.</param>
/// <param name="Genres">Genre classifications.</param>
public sealed record MaterializedMetadata(
    string? Title,
    string? OriginalTitle,
    string? SortTitle,
    string? Language,
    string? Overview,
    DateTime? PublishDate,
    TimeSpan? Duration,
    string? ProductionStatus,
    DateTime? Established,
    DateTime? Dissolved,
    DateTime? BirthDate,
    DateTime? DeathDate,
    IReadOnlyDictionary<string, string?>? CustomMetadata,
    IReadOnlyList<ExternalIdMetadata>? ExternalIds,
    IReadOnlyList<ReferenceMetadata>? References,
    IReadOnlyList<RelationshipMetadata>? Relationships,
    IReadOnlyList<ImageMetadata>? Images,
    IReadOnlyList<PreviewMetadata>? Previews,
    IReadOnlyList<AlternateTextMetadata>? AlternateTexts,
    IReadOnlyList<ContentRatingMetadata>? ContentRatings,
    IReadOnlyList<AwardMetadata>? Awards,
    IReadOnlyList<GenreMetadata>? Genres);
