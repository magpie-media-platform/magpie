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
    /// The unique id of the indexer.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the feed.
    /// </summary>
    public Guid? FeedId { get; set; }

    /// <summary>
    /// The id of the extension that supplies the indexer.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The name of the provider type.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// The display name of the indexer.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The description of the indexer.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The availability of the indexer.
    /// </summary>
    public IndexerAvailability Availability { get; set; }

    /// <summary>
    /// The categories of the indexer.
    /// </summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// The language codes of the indexer.
    /// </summary>
    public List<string> Languages { get; set; } = [];

    /// <summary>
    /// The content types that the indexer supports.
    /// </summary>
    public List<string> SupportedContentTypes { get; set; } = [];

    /// <summary>
    /// The type of the indexer definition.
    /// </summary>
    public string? DefinitionType { get; set; }

    /// <summary>
    /// The schema data of the indexer definition.
    /// </summary>
    public string? SchemaData { get; set; }

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

    /// <summary>
    /// The connections to the indexer.
    /// </summary>
    public ICollection<IndexerConnection> Connections { get; } = [];
}
