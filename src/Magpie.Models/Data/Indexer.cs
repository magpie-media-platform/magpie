using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A searchable index of <see cref="Release"/>s.
/// </summary>
public record Indexer
{
    /// <summary>
    /// The indexer id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The feed the indexer was synchronized from, if applicable.
    /// </summary>
    public Guid? FeedId { get; set; }

    /// <summary>
    /// The extension that defines the indexer provider.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The indexer provider type.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// The display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// A description of the indexer.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The availability of the indexer.
    /// </summary>
    public IndexerAvailability Availability { get; set; }

    /// <summary>
    /// A list of categories the indexer supports.
    /// </summary>
    /// <remarks>
    /// The categories are stored as serialized JSON
    /// </remarks>
    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// A list of languages the indexer supports.
    /// </summary>
    /// <remarks>
    /// The languages are stored as serialized JSON
    /// </remarks>
    public List<string> Languages { get; set; } = [];

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

    /// <summary>
    /// Configured connections to the indexer.
    /// </summary>
    public ICollection<IndexerConnection> Connections { get; } = [];

}

