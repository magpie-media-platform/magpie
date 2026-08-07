using System.Collections.Generic;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record QueryPlan
{
    public PlanNode? Root { get; init; }

    public required int RequestedPage { get; init; }

    public int MaxResults { get; init; } = QueryLimits.DefaultMaxResults;

    public required Settings Settings { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public required string Fingerprint { get; init; }

    public QueryTraceProvenance TraceProvenance { get; init; } = new();
}
