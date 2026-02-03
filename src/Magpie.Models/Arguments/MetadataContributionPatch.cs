using System;
using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="MetadataContribution"/>.
/// </summary>
public sealed record ContributionPatch
{
    /// <summary>
    /// The <see cref="MetadataContribution.Id"/> of the item to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.Ordinal"/> to patch.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.Locked"/> to patch.
    /// </summary>
    public ScalarPatch<bool>? Locked { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.Title"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? Title { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.OriginalTitle"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? OriginalTitle { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.SortTitle"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? SortTitle { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.Language"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? Language { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.Overview"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? Overview { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.PublishDate"/> to patch.
    /// </summary>
    public ScalarPatch<DateTime?>? PublishDate { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.Duration"/> to patch.
    /// </summary>
    public ScalarPatch<TimeSpan?>? Duration { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.ProductionStatus"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? ProductionStatus { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.Established"/> to patch.
    /// </summary>
    public ScalarPatch<DateTime?>? Established { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.Dissolved"/> to patch.
    /// </summary>
    public ScalarPatch<DateTime?>? Dissolved { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.BirthDate"/> to patch.
    /// </summary>
    public ScalarPatch<DateTime?>? BirthDate { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.DeathDate"/> to patch.
    /// </summary>
    public ScalarPatch<DateTime?>? DeathDate { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.AlternateTexts"/> to patch.
    /// </summary>
    public CollectionPatch<AlternateText, Guid, AlternateTextPatch>? AlternateTexts { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.Awards"/> to patch.
    /// </summary>
    public CollectionPatch<Award, Guid, AwardPatch>? Awards { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.ContentRatings"/> to patch.
    /// </summary>
    public CollectionPatch<ContentRating, Guid, ContentRatingPatch>? ContentRatings { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.CustomMetadata"/> to patch.
    /// </summary>
    public ScalarPatch<IReadOnlyDictionary<string, string?>>? CustomMetadata { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.ExternalIds"/> to patch.
    /// </summary>
    public CollectionPatch<ExternalId, Guid, ExternalIdPatch>? ExternalIds { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.Genres"/> to patch.
    /// </summary>
    public SetPatch<string, string>? Genres { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.Images"/> to patch.
    /// </summary>
    public CollectionPatch<Image, Guid, ImagePatch>? Images { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.Previews"/> to patch.
    /// </summary>
    public CollectionPatch<Preview, Guid, PreviewPatch>? Previews { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.References"/> to patch.
    /// </summary>
    public CollectionPatch<Reference, Guid, ReferencePatch>? References { get; init; }

    /// <summary>
    /// The <see cref="MetadataContribution.Relationships"/> to patch.
    /// </summary>
    public CollectionPatch<Relationship, Guid, RelationshipPatch>? Relationships { get; init; }
}
