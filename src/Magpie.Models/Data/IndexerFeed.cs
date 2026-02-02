using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A feed that provides available indexers.
/// </summary>
public record IndexerFeed
{
    /// <summary>
    /// The indexer feed id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The extension that defines the feed provider.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The indexer feed provider type.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// The display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// A description of the feed.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// A set of provider-specific settings.
    /// </summary>
    /// <remarks>
    /// The settings are stored as serialized JSON.
    /// </remarks>
    public Dictionary<string, string> Settings { get; set; } = [];

    /// <summary>
    /// Whether the feed is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// When the feed was last synchronized.
    /// </summary>
    public DateTime? LastSync { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user that created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// When the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user that last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
