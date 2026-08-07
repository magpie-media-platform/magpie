using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record ResidualEvaluationResult
{
    public required IReadOnlyList<SearchHit> Hits { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
