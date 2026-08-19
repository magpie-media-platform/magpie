using System;
using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models.Releases;

/// <summary>
/// One release of a search phase with the connections that reported it.
/// </summary>
public sealed record ReleaseSearchCandidate
{
    /// <summary>
    /// The stable key of the coalesced release.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The release that the phase found.
    /// </summary>
    public required Release Release { get; init; }

    /// <summary>
    /// The ids of the indexer connections that reported the release.
    /// </summary>
    public IReadOnlyList<Guid> ConnectionIds { get; init; } = [];
}
