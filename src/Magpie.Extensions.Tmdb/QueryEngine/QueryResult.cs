using System.Collections.Generic;

using Magpie.Models.MetadataSearch;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record QueryResult
{
    internal required bool Supported { get; init; }

    internal required IReadOnlyList<SearchHit> Hits { get; init; }

    internal required IReadOnlyList<string> Warnings { get; init; }

    internal required ProviderSearchPageInfo PageInfo { get; init; }
}
