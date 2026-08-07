using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A feed that provides available indexers.
/// </summary>
public record IndexerFeed
{
    /// <summary>
    /// The unique id of the indexer feed.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the extension that supplies the feed.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The name of the provider type.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// The display name of the feed.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The description of the feed.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The provider-specific settings of the feed.
    /// </summary>
    public Dictionary<string, string> Settings { get; set; } = [];

    /// <summary>
    /// Whether the feed is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The UTC time of the last sync.
    /// </summary>
    public DateTime? LastSync { get; set; }

    /// <summary>
    /// The UTC time the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
