using System;
using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// The request that searches a set of indexer connections directly.
/// </summary>
public sealed record IndexerSearchRequest
{
    /// <summary>
    /// The ids of the indexer connections to search.
    /// </summary>
    public IReadOnlyList<Guid> ConnectionIds { get; init; } = [];

    /// <summary>
    /// The query to send to each connection.
    /// </summary>
    public required ReleaseSearchRequest Query { get; init; }
}
