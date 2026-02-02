using System;
using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="MetadataContribution"/>.
/// </summary>
/// <param name="Id">The <see cref="MetadataContribution.Id"/> of the item to patch.</param>
/// <param name="Ordinal">The <see cref="MetadataContribution.Ordinal"/> patch</param>
/// <param name="Locked">The <see cref="MetadataContribution.Locked"/> patch</param>
/// <param name="Title">The <see cref="MetadataContribution.Title"/> patch</param>
/// <param name="OriginalTitle">The <see cref="MetadataContribution.OriginalTitle"/> patch</param>
/// <param name="SortTitle">The <see cref="MetadataContribution.SortTitle"/> patch</param>
/// <param name="Language">The <see cref="MetadataContribution.Language"/> patch</param>
/// <param name="Overview">The <see cref="MetadataContribution.Overview"/> patch</param>
/// <param name="PublishDate">The <see cref="MetadataContribution.PublishDate"/> patch</param>
/// <param name="Duration">The <see cref="MetadataContribution.Duration"/> patch</param>
/// <param name="ProductionStatus">The <see cref="MetadataContribution.ProductionStatus"/> patch</param>
/// <param name="Established">The <see cref="MetadataContribution.Established"/> patch</param>
/// <param name="Dissolved">The <see cref="MetadataContribution.Dissolved"/> patch</param>
/// <param name="BirthDate">The <see cref="MetadataContribution.BirthDate"/> patch</param>
/// <param name="DeathDate">The <see cref="MetadataContribution.DeathDate"/> patch</param>
/// <param name="AlternateTexts">The <see cref="MetadataContribution.AlternateTexts"/> patch</param>
/// <param name="Awards">The <see cref="MetadataContribution.Awards"/> patch</param>
/// <param name="ContentRatings">The <see cref="MetadataContribution.ContentRatings"/> patch</param>
/// <param name="CustomMetadata">The <see cref="MetadataContribution.CustomMetadata"/> patch</param>
/// <param name="ExternalIds">The <see cref="MetadataContribution.ExternalIds"/> patch</param>
/// <param name="Genres">The <see cref="MetadataContribution.Genres"/> patch</param>
/// <param name="Images">The <see cref="MetadataContribution.Images"/> patch</param>
/// <param name="Previews">The <see cref="MetadataContribution.Previews"/> patch</param>
/// <param name="References">The <see cref="MetadataContribution.References"/> patch</param>
/// <param name="Relationships">The <see cref="MetadataContribution.Relationships"/> patch</param>
public sealed record ContributionPatch(
    Guid Id,
    ScalarPatch<int>? Ordinal = null,
    ScalarPatch<bool>? Locked = null,
    ScalarPatch<string?>? Title = null,
    ScalarPatch<string?>? OriginalTitle = null,
    ScalarPatch<string?>? SortTitle = null,
    ScalarPatch<string?>? Language = null,
    ScalarPatch<string?>? Overview = null,
    ScalarPatch<DateTime?>? PublishDate = null,
    ScalarPatch<TimeSpan?>? Duration = null,
    ScalarPatch<string?>? ProductionStatus = null,
    ScalarPatch<DateTime?>? Established = null,
    ScalarPatch<DateTime?>? Dissolved = null,
    ScalarPatch<DateTime?>? BirthDate = null,
    ScalarPatch<DateTime?>? DeathDate = null,
    CollectionPatch<AlternateText, Guid, AlternateTextPatch>? AlternateTexts = null,
    CollectionPatch<Award, Guid, AwardPatch>? Awards = null,
    CollectionPatch<ContentRating, Guid, ContentRatingPatch>? ContentRatings = null,
    ScalarPatch<IReadOnlyDictionary<string, string?>>? CustomMetadata = null,
    CollectionPatch<ExternalId, Guid, ExternalIdPatch>? ExternalIds = null,
    SetPatch<string, string>? Genres = null,
    CollectionPatch<Image, Guid, ImagePatch>? Images = null,
    CollectionPatch<Preview, Guid, PreviewPatch>? Previews = null,
    CollectionPatch<Reference, Guid, ReferencePatch>? References = null,
    CollectionPatch<Relationship, Guid, RelationshipPatch>? Relationships = null);
