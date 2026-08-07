using System;
using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a <see cref="MetadataContribution"/>.
/// </summary>
public record MetadataContributionPatch
{
    /// <summary>
    /// The patch for <see cref="MetadataContribution.Title"/>.
    /// </summary>
    public ScalarPatch<string?>? Title { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.OriginalTitle"/>.
    /// </summary>
    public ScalarPatch<string?>? OriginalTitle { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.SortTitle"/>.
    /// </summary>
    public ScalarPatch<string?>? SortTitle { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.Language"/>.
    /// </summary>
    public ScalarPatch<string?>? Language { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.Overview"/>.
    /// </summary>
    public ScalarPatch<string?>? Overview { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.PublishDate"/>.
    /// </summary>
    public ScalarPatch<DateTime?>? PublishDate { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.Duration"/>.
    /// </summary>
    public ScalarPatch<TimeSpan?>? Duration { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.SequenceNumber"/>.
    /// </summary>
    public ScalarPatch<int?>? SequenceNumber { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.ProductionStatus"/>.
    /// </summary>
    public ScalarPatch<string?>? ProductionStatus { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.Established"/>.
    /// </summary>
    public ScalarPatch<DateTime?>? Established { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.Dissolved"/>.
    /// </summary>
    public ScalarPatch<DateTime?>? Dissolved { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.BirthDate"/>.
    /// </summary>
    public ScalarPatch<DateTime?>? BirthDate { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.DeathDate"/>.
    /// </summary>
    public ScalarPatch<DateTime?>? DeathDate { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.AlternateTexts"/>.
    /// </summary>
    public CollectionPatch<AlternateText, Guid, AlternateTextPatch>? AlternateTexts { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.Awards"/>.
    /// </summary>
    public CollectionPatch<Award, Guid, AwardPatch>? Awards { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.ContentRatings"/>.
    /// </summary>
    public CollectionPatch<ContentRating, Guid, ContentRatingPatch>? ContentRatings { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.CustomMetadata"/>.
    /// </summary>
    public ScalarPatch<IReadOnlyDictionary<string, string?>>? CustomMetadata { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.ExternalIds"/>.
    /// </summary>
    public CollectionPatch<ExternalId, Guid, ExternalIdPatch>? ExternalIds { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.Genres"/>, expressed as genre names.
    /// </summary>
    public SetPatch<string, string>? Genres { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.Images"/>.
    /// </summary>
    public CollectionPatch<Image, Guid, ImagePatch>? Images { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.Previews"/>.
    /// </summary>
    public CollectionPatch<Preview, Guid, PreviewPatch>? Previews { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.References"/>.
    /// </summary>
    public CollectionPatch<Reference, Guid, ReferencePatch>? References { get; init; }

    /// <summary>
    /// The patch for <see cref="MetadataContribution.Relationships"/> as <see cref="RelationshipPatch"/> items.
    /// </summary>
    public CollectionPatch<RelationshipPatch, Guid, RelationshipPatch>? Relationships { get; init; }
}
