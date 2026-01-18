using System.Collections.Generic;

namespace Magpie.Models.Indexers;

/// <summary>
/// Ephemeral status of a release from an indexer.
/// </summary>
/// <param name="Release">The underlying release.</param>
/// <param name="Grabs">The number of completed downloads.</param>
/// <param name="Seeders">The number of seeders (torrents).</param>
/// <param name="Leechers">The number of leechers (torrents).</param>
/// <param name="Peers">The number of peers (torrents).</param>
/// <param name="Properties">Provider-specific ephemeral properties.</param>
public sealed record TrackedRelease(
    Release Release,
    int? Grabs,
    int? Seeders,
    int? Leechers,
    int? Peers,
    IReadOnlyDictionary<string, string?>? Properties);
