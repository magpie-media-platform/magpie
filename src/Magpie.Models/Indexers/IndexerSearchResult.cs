using System;
using System.Collections.Generic;

namespace Magpie.Models.Indexers;

/// <summary>
/// Search results from an indexer.
/// </summary>
/// <param name="IndexerId">The source indexer.</param>
/// <param name="Query">The search query.</param>
/// <param name="Releases">The releases found.</param>
public sealed record IndexerSearchResult(
    Guid IndexerId,
    string Query,
    IReadOnlyList<TrackedRelease> Releases);
