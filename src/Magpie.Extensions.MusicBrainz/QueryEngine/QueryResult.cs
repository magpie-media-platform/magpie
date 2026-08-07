using System.Collections.Generic;

using Magpie.Extensions.MusicBrainz.Mapping;
using Magpie.Models.MetadataSearch;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record QueryResult
{
    public required bool Supported { get; init; }

    public required IReadOnlyList<SearchHit> Hits { get; init; }

    public required ProviderSearchPageInfo PageInfo { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
