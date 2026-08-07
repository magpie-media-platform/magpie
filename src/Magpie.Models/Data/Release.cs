using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A downloadable instance of a <see cref="ContentItem"/>.
/// </summary>
public record Release
{
    /// <summary>
    /// The unique id of the release.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the indexer that reported the release.
    /// </summary>
    public Guid? IndexerId { get; set; }

    /// <summary>
    /// The transfer protocol of the release, such as torrent or usenet.
    /// </summary>
    public required string Protocol { get; set; }

    /// <summary>
    /// The id of the release at the indexer.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// The release title as the indexer reports it.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// The date when the indexer published the release.
    /// </summary>
    public DateTime PublishDate { get; set; }

    /// <summary>
    /// The size of the release in bytes.
    /// </summary>
    public long? Size { get; set; }

    /// <summary>
    /// The URL of the details page at the indexer.
    /// </summary>
    public Uri? InfoUrl { get; set; }

    /// <summary>
    /// The indexer categories of the release.
    /// </summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// The languages of the release.
    /// </summary>
    public List<string> Languages { get; set; } = [];

    /// <summary>
    /// The provider-specific properties of the release.
    /// </summary>
    public string? Properties { get; set; }

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The UTC time when an indexer search last saw the release.
    /// </summary>
    public DateTime ObservedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time when the record last changed.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last changed the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }

    /// <summary>
    /// The downloads that grabbed the release.
    /// </summary>
    public ICollection<Download> Downloads { get; } = [];

    /// <summary>
    /// The ids at external providers.
    /// </summary>
    public ICollection<ExternalId> ExternalIds { get; } = [];

    /// <summary>
    /// The keys that identify the release across providers.
    /// </summary>
    public ICollection<ReleaseIdentityKey> IdentityKeys { get; } = [];

    /// <summary>
    /// The indexer observations that can supply the release.
    /// </summary>
    public ICollection<ReleaseSource> Sources { get; } = [];

    /// <summary>
    /// The content hierarchy nodes that the release covers.
    /// </summary>
    public ICollection<ReleaseScope> Scope { get; } = [];
}
