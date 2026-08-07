using System;
using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// The API representation of a configured indexer feed.
/// </summary>
public sealed record IndexerFeedResponse
{
    /// <summary>
    /// The id of the indexer feed.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The id of the extension that provides the feed.
    /// </summary>
    public Guid? ExtensionId { get; init; }

    /// <summary>
    /// The provider type of the feed.
    /// </summary>
    public required string ProviderType { get; init; }

    /// <summary>
    /// The name of the feed.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The description of the feed.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether the feed is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The time of the last synchronization of the feed.
    /// </summary>
    public DateTime? LastSync { get; init; }

    /// <summary>
    /// The settings of the feed, keyed by setting key.
    /// </summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; init; }

    /// <summary>
    /// The UTC time when the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; init; }

    /// <summary>
    /// The id of the user who last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; init; }
}
