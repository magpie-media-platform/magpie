using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// An indexer observation that can supply a logical release.
/// </summary>
public record ReleaseSource
{
    /// <summary>
    /// The unique id of the source.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the logical release.
    /// </summary>
    public Guid ReleaseId { get; set; }

    /// <summary>
    /// The stable key of the source within the logical release.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// The id of the indexer that supplied the observation.
    /// </summary>
    public Guid? IndexerId { get; set; }

    /// <summary>
    /// The id of the indexer connection that supplied the observation.
    /// </summary>
    public Guid? IndexerConnectionId { get; set; }

    /// <summary>
    /// The transfer protocol of the source.
    /// </summary>
    public required string Protocol { get; set; }

    /// <summary>
    /// The id of the observation at the indexer.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// The release title reported by the indexer.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// The publication date reported by the indexer.
    /// </summary>
    public DateTime PublishDate { get; set; }

    /// <summary>
    /// The size reported by the indexer in bytes.
    /// </summary>
    public long? Size { get; set; }

    /// <summary>
    /// The URL used to grab the source.
    /// </summary>
    public Uri? InfoUrl { get; set; }

    /// <summary>
    /// The categories reported by the indexer.
    /// </summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// The languages reported by the indexer.
    /// </summary>
    public List<string> Languages { get; set; } = [];

    /// <summary>
    /// The provider-specific properties of the observation.
    /// </summary>
    public string? Properties { get; set; }

    /// <summary>
    /// The UTC time when the source was first observed.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The UTC time when the source was last observed.
    /// </summary>
    public DateTime ObservedAt { get; set; }

    /// <summary>
    /// The logical release supplied by the source.
    /// </summary>
    public Release? Release { get; set; }

    /// <summary>
    /// The downloads that used the source.
    /// </summary>
    public ICollection<Download> Downloads { get; } = [];
}
