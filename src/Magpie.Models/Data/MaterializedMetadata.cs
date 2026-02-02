using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// Aggregated metadata materialized from provider contributions.
/// </summary>
public sealed record MaterializedMetadata
{
    /// <summary>
    /// The <see cref="MetadataContainer.Id"/> of the metadata.
    /// </summary>
    public Guid ContainerId { get; init; }

    /// <summary>
    /// The related <see cref="ContentHierarchyItem.Id"/>, if applicable.
    /// </summary>
    public Guid? ContentHierarchyItemId { get; init; }

    /// <summary>
    /// The related <see cref="Subject.Id"/>, if applicable.
    /// </summary>
    public Guid? SubjectId { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.Title"/>.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.OriginalTitle"/>.
    /// </summary>
    public string? OriginalTitle { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.SortTitle"/>.
    /// </summary>
    public string? SortTitle { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.Language"/>.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.Overview"/>.
    /// </summary>
    public string? Overview { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.PublishDate"/>.
    /// </summary>
    public DateTime? PublishDate { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.Duration"/>.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.ProductionStatus"/>.
    /// </summary>
    public string? ProductionStatus { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.Established"/>.
    /// </summary>
    public DateTime? Established { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.Dissolved"/>.
    /// </summary>
    public DateTime? Dissolved { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.BirthDate"/>.
    /// </summary>
    public DateTime? BirthDate { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.DeathDate"/>.
    /// </summary>
    public DateTime? DeathDate { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.CustomMetadata"/>.
    /// </summary>
    public IEnumerable<CustomMetadataValue>? CustomMetadata { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.ExternalIds"/>.
    /// </summary>
    public IEnumerable<ExternalId>? ExternalIds { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.References"/>.
    /// </summary>
    public IEnumerable<Reference>? References { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.Relationships"/>.
    /// </summary>
    public IEnumerable<Relationship>? Relationships { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.Images"/>.
    /// </summary>
    public IEnumerable<Image>? Images { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.Previews"/>.
    /// </summary>
    public IEnumerable<Preview>? Previews { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.AlternateTexts"/>.
    /// </summary>
    public IEnumerable<AlternateText>? AlternateTexts { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.ContentRatings"/>.
    /// </summary>
    public IEnumerable<ContentRating>? ContentRatings { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.Awards"/>.
    /// </summary>
    public IEnumerable<Award>? Awards { get; init; }

    /// <summary>
    /// The materialized <see cref="MetadataContribution.Genres"/>.
    /// </summary>
    public IEnumerable<Genre>? Genres { get; init; }
}
