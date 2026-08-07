using Magpie.Extensions.Tmdb.QueryEngine;

namespace Magpie.Extensions.Tmdb;

internal sealed record RequestContext
{
    internal required string ApiKey { get; init; }

    internal required string Language { get; init; }

    internal ExecutionTrace? ExecutionTrace { get; init; }

    internal int? PlanNodeId { get; init; }
}
