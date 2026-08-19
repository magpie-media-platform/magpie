using System;
using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models.Api;

/// <summary>
/// The API representation of one raw release of an indexer search.
/// </summary>
public sealed record IndexerSearchCandidate
{
    /// <summary>
    /// The stable key of the coalesced release.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The release that the indexers reported.
    /// </summary>
    public required Release Release { get; init; }

    /// <summary>
    /// The ids of the indexer connections that reported the release.
    /// </summary>
    public IReadOnlyList<Guid> ConnectionIds { get; init; } = [];
}
