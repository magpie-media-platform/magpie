using System;
using System.Collections.Generic;

namespace Magpie.Models.Metadata;

/// <summary>
/// A search result for metadata from an external source.
/// </summary>
/// <param name="Provider">The provider identifier.</param>
/// <param name="ExternalIds">The external identifiers from the provider.</param>
/// <param name="Name">The name of the content or subject.</param>
/// <param name="Language">The primary language.</param>
/// <param name="ProductionStatus">The production status (for content).</param>
/// <param name="PublishDate">The publish date (for content).</param>
/// <param name="BirthDate">The birth date (for people).</param>
/// <param name="DeathDate">The death date (for people).</param>
/// <param name="Established">When the entity was established (for organizations/studios).</param>
/// <param name="Dissolved">When the entity was dissolved (for organizations/studios).</param>
/// <param name="Overview">A brief overview or description.</param>
/// <param name="PrimaryImage">The primary image.</param>
/// <param name="Awards">Awards received (award name to value, e.g., year or category).</param>
/// <param name="Genres">Genres associated with the content or subject.</param>
/// <param name="ContentRatings">Content ratings (country/region to rating value).</param>
public sealed record MetadataSearchResult(
    string Provider,
    IReadOnlyList<ExternalIdMetadata> ExternalIds,
    string Name,
    string? Language,
    string? ProductionStatus,
    DateTime? PublishDate,
    DateTime? BirthDate,
    DateTime? DeathDate,
    DateTime? Established,
    DateTime? Dissolved,
    string? Overview,
    ImageMetadata? PrimaryImage,
    IReadOnlyDictionary<string, string> Awards,
    IReadOnlyList<string> Genres,
    IReadOnlyDictionary<string, string> ContentRatings);
