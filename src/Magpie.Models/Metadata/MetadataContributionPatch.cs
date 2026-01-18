using System;
using System.Collections.Generic;

namespace Magpie.Models.Metadata;

/// <summary>
/// Patch for <see cref="MetadataContribution"/>.
/// </summary>
public sealed record MetadataContributionPatch
{
    /// <inheritdoc cref="MetadataContribution.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="MetadataContribution.Ordinal" />
    public FieldPatch<int>? Ordinal { get; init; }
    /// <inheritdoc cref="MetadataContribution.Locked" />
    public FieldPatch<bool>? Locked { get; init; }
    /// <inheritdoc cref="MetadataContribution.Provider" />
    public FieldPatch<string?>? Provider { get; init; }
    /// <inheritdoc cref="MetadataContribution.Attribution" />
    public FieldPatch<string?>? Attribution { get; init; }
    /// <inheritdoc cref="MetadataContribution.Title" />
    public FieldPatch<string?>? Title { get; init; }
    /// <inheritdoc cref="MetadataContribution.OriginalTitle" />
    public FieldPatch<string?>? OriginalTitle { get; init; }
    /// <inheritdoc cref="MetadataContribution.SortTitle" />
    public FieldPatch<string?>? SortTitle { get; init; }
    /// <inheritdoc cref="MetadataContribution.Language" />
    public FieldPatch<string?>? Language { get; init; }
    /// <inheritdoc cref="MetadataContribution.Overview" />
    public FieldPatch<string?>? Overview { get; init; }
    /// <inheritdoc cref="MetadataContribution.PublishDate" />
    public FieldPatch<DateTime?>? PublishDate { get; init; }
    /// <inheritdoc cref="MetadataContribution.Duration" />
    public FieldPatch<TimeSpan?>? Duration { get; init; }
    /// <inheritdoc cref="MetadataContribution.ProductionStatus" />
    public FieldPatch<string?>? ProductionStatus { get; init; }
    /// <inheritdoc cref="MetadataContribution.Established" />
    public FieldPatch<DateTime?>? Established { get; init; }
    /// <inheritdoc cref="MetadataContribution.Dissolved" />
    public FieldPatch<DateTime?>? Dissolved { get; init; }
    /// <inheritdoc cref="MetadataContribution.BirthDate" />
    public FieldPatch<DateTime?>? BirthDate { get; init; }
    /// <inheritdoc cref="MetadataContribution.DeathDate" />
    public FieldPatch<DateTime?>? DeathDate { get; init; }
    /// <inheritdoc cref="MetadataContribution.CustomMetadata" />
    public FieldPatch<IReadOnlyDictionary<string, string?>>? CustomMetadata { get; init; }
    /// <inheritdoc cref="MetadataContribution.ExternalIds" />
    public CollectionPatch<ExternalIdMetadata, Guid, ExternalIdMetadataPatch>? ExternalIds { get; init; }
    /// <inheritdoc cref="MetadataContribution.References" />
    public CollectionPatch<ReferenceMetadata, Guid, ReferenceMetadataPatch>? References { get; init; }
    /// <inheritdoc cref="MetadataContribution.Relationships" />
    public CollectionPatch<RelationshipMetadata, Guid, RelationshipMetadataPatch>? Relationships { get; init; }
    /// <inheritdoc cref="MetadataContribution.Images" />
    public CollectionPatch<ImageMetadata, Guid, ImageMetadataPatch>? Images { get; init; }
    /// <inheritdoc cref="MetadataContribution.Previews" />
    public CollectionPatch<PreviewMetadata, Guid, PreviewMetadataPatch>? Previews { get; init; }
    /// <inheritdoc cref="MetadataContribution.AlternateTexts" />
    public CollectionPatch<AlternateTextMetadata, Guid, AlternateTextMetadataPatch>? AlternateTexts { get; init; }
    /// <inheritdoc cref="MetadataContribution.ContentRatings" />
    public CollectionPatch<ContentRatingMetadata, Guid, ContentRatingMetadataPatch>? ContentRatings { get; init; }
    /// <inheritdoc cref="MetadataContribution.Awards" />
    public CollectionPatch<AwardMetadata, Guid, AwardMetadataPatch>? Awards { get; init; }
    /// <inheritdoc cref="MetadataContribution.Genres" />
    public CollectionPatch<GenreMetadata, Guid, GenreMetadataPatch>? Genres { get; init; }
}
