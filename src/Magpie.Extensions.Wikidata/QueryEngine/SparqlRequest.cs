using System;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record SparqlRequest
{
    public required string QueryText { get; init; }

    public required string CanonicalCacheKey { get; init; }

    public required TimeSpan Timeout { get; init; }
}
