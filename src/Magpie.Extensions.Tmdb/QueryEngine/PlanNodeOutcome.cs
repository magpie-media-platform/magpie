using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record PlanNodeOutcome
{
    internal required int NodeId { get; init; }

    internal required string Status { get; init; }

    internal string? Reason { get; init; }

    internal int RowsIn { get; init; }

    internal int RowsOut { get; init; }

    internal IReadOnlyDictionary<string, int> Rejections { get; init; } =
        new Dictionary<string, int>();
}
