using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record QueryPlan
{
    internal required PlanNode Root { get; init; }

    internal required IReadOnlyList<string> Warnings { get; init; }

    internal required int RequestedPage { get; init; }

    internal int? MaximumResults { get; init; }

    internal required string DefaultLanguagePreference { get; init; }

    internal required string Fingerprint { get; init; }
}
