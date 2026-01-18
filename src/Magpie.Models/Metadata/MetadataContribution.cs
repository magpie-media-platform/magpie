using System;
using System.Collections.Generic;

namespace Magpie.Models.Metadata;

/// <summary>
/// A metadata contribution from a provider.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Ordinal">The ordinal of the metadata contribution relative to other contributions.</param>
/// <param name="Locked">Whether the metadata contribution is locked and cannot be modified.</param>
/// <param name="Provider">The provider that supplied this contribution.</param>
/// <param name="DateAdded">The date the metadata contribution was added.</param>
/// <param name="DateLastRefreshed">The date the metadata contribution was last refreshed by the provider.</param>
/// <param name="Attribution">The attribution for the source of the metadata.</param>
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
/// <param name="BirthDate">The date of birth.</param>
/// <param name="DeathDate">The date of death.</param>
/// <param name="CustomMetadata">Additional custom metadata as key-value pairs.</param>
/// <param name="ExternalIds">External identifiers from this and other providers.</param>
/// <param name="References">External references and informational links.</param>
/// <param name="Relationships">Relationships to subjects.</param>
/// <param name="Images">Images associated with this item.</param>
/// <param name="Previews">Previews associated with this item.</param>
/// <param name="AlternateTexts">Alternate text entries.</param>
/// <param name="ContentRatings">Content ratings from various bodies.</param>
/// <param name="Awards">Award nominations and wins.</param>
/// <param name="Genres">Genres.</param>
public sealed record MetadataContribution(
    Guid Id,
    int Ordinal,
    bool Locked,
    string? Provider,
    DateTimeOffset DateAdded,
    DateTimeOffset? DateLastRefreshed,
    string? Attribution,
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
    IReadOnlyDictionary<string, string?> CustomMetadata,
    IReadOnlyList<ExternalIdMetadata>? ExternalIds,
    IReadOnlyList<ReferenceMetadata>? References,
    IReadOnlyList<RelationshipMetadata>? Relationships,
    IReadOnlyList<ImageMetadata>? Images,
    IReadOnlyList<PreviewMetadata>? Previews,
    IReadOnlyList<AlternateTextMetadata>? AlternateTexts,
    IReadOnlyList<ContentRatingMetadata>? ContentRatings,
    IReadOnlyList<AwardMetadata>? Awards,
    IReadOnlyList<GenreMetadata>? Genres);
