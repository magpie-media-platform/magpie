using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Api;

/// <summary>
/// The API representation of a configured indexer.
/// </summary>
public sealed record IndexerResponse
{
    /// <summary>
    /// The id of the indexer.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The id of the indexer feed.
    /// </summary>
    public Guid? FeedId { get; init; }

    /// <summary>
    /// The id of the extension that provides the indexer.
    /// </summary>
    public Guid? ExtensionId { get; init; }

    /// <summary>
    /// The provider type of the indexer.
    /// </summary>
    public required string ProviderType { get; init; }

    /// <summary>
    /// The name of the indexer.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The description of the indexer.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The availability of the indexer.
    /// </summary>
    public IndexerAvailability Availability { get; init; }

    /// <summary>
    /// The categories that the indexer supports.
    /// </summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>
    /// The languages that the indexer supports.
    /// </summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>
    /// The content types that the indexer supports.
    /// </summary>
    public IReadOnlyList<string> SupportedContentTypes { get; init; } = [];

    /// <summary>
    /// The definition type of the indexer.
    /// </summary>
    public string? DefinitionType { get; init; }

    /// <summary>
    /// The number of connections to the indexer.
    /// </summary>
    public int ConnectionCount { get; init; }

    /// <summary>
    /// The parameter sets that the indexer accepts together, one set for each query mode.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> SearchFacets { get; init; } = [];

    /// <summary>
    /// True when the indexer accepts raw text.
    /// </summary>
    public bool SupportsKeywordSearch { get; init; }

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
