using System;
using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// The search capabilities that one indexer advertises.
/// </summary>
public sealed record IndexerSearchCapabilities
{
    /// <summary>
    /// The id of the indexer.
    /// </summary>
    public required Guid IndexerId { get; init; }

    /// <summary>
    /// The parameter sets that the indexer accepts together, one set for each query mode.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> SearchFacets { get; init; } = [];

    /// <summary>
    /// True when the indexer accepts raw text.
    /// </summary>
    public bool SupportsKeywordSearch { get; init; }
}
