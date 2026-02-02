using System;
using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models;

/// <summary>
/// A search result for metadata from an external source.
/// </summary>
/// <param name="ProviderType"><inheritdoc cref="MetadataContribution.ProviderType" path="/summary"/>.</param>
/// <param name="Source"><inheritdoc cref="MetadataContribution.Source" path="/summary"/>.</param>
/// <param name="ThumbnailUrl">The thumbnail url for the content.</param>
/// <param name="Title"><inheritdoc cref="MetadataContribution.Title" path="/summary"/>.</param>
/// <param name="Language"><inheritdoc cref="MetadataContribution.Language" path="/summary"/>.</param> 
/// <param name="Overview"><inheritdoc cref="MetadataContribution.Overview" path="/summary"/>.</param>
/// <param name="PublishDate"><inheritdoc cref="MetadataContribution.PublishDate" path="/summary"/>.</param>
/// <param name="Duration"><inheritdoc cref="MetadataContribution.Duration" path="/summary"/>.</param>
/// <param name="ProductionStatus"><inheritdoc cref="MetadataContribution.ProductionStatus" path="/summary"/>.</param>
/// <param name="Established"><inheritdoc cref="MetadataContribution.Established" path="/summary"/>.</param>
/// <param name="Dissolved"><inheritdoc cref="MetadataContribution.Dissolved" path="/summary"/>.</param>
/// <param name="BirthDate"><inheritdoc cref="MetadataContribution.BirthDate" path="/summary"/>.</param>
/// <param name="DeathDate"><inheritdoc cref="MetadataContribution.DeathDate" path="/summary"/>.</param>
/// <param name="Awards"><inheritdoc cref="MetadataContribution.Awards" path="/summary"/>.</param>
/// <param name="ContentRatings"><inheritdoc cref="MetadataContribution.ContentRatings" path="/summary"/>.</param>
/// <param name="ExternalIds"><inheritdoc cref="MetadataContribution.ExternalIds" path="/summary"/>.</param>
/// <param name="Genres"><inheritdoc cref="MetadataContribution.Genres" path="/summary"/>.</param>
public sealed record MetadataSearchResultItem(
    string ProviderType,
    string Source,
    Uri? ThumbnailUrl,
    string? Title,
    string? Language,
    string? Overview,
    DateTime? PublishDate,
    TimeSpan? Duration,
    string? ProductionStatus,
    DateTime? Established,
    DateTime? Dissolved,
    DateTime? BirthDate,
    DateTime? DeathDate,
    IReadOnlyDictionary<string, string> Awards,
    IReadOnlyDictionary<string, string> ContentRatings,
    IEnumerable<ExternalId> ExternalIds,
    IEnumerable<string> Genres);
