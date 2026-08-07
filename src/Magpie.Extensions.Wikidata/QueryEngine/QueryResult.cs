using System.Collections.Generic;

using Magpie.Models.MetadataSearch;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record QueryResult
{
    public bool Supported { get; init; } = true;

    public IReadOnlyList<SearchHit> Hits { get; init; } = [];

    public required ProviderSearchPageInfo PageInfo { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<CriterionDisposition> CriterionDispositions { get; init; } = [];

    public QueryExecution Execution { get; init; } = null!;
}
