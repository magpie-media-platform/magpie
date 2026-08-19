using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// The API representation of the result of an indexer search.
/// </summary>
public sealed record IndexerSearchResponse
{
    /// <summary>
    /// The releases that the search found, in publish date descending order.
    /// </summary>
    public IReadOnlyList<IndexerSearchCandidate> Candidates { get; init; } = [];

    /// <summary>
    /// The outcome of each selected indexer connection.
    /// </summary>
    public IReadOnlyList<IndexerSearchConnectionResult> Connections { get; init; } = [];

    /// <summary>
    /// The number of connections that the search sent the query to.
    /// </summary>
    public int ConnectionsAttempted { get; init; }

    /// <summary>
    /// The number of selected connections that the search did not query.
    /// </summary>
    public int ConnectionsSkipped { get; init; }
}
