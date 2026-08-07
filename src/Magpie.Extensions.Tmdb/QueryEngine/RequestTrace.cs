namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record RequestTrace
{
    internal required int Id { get; init; }

    internal int? PlanNodeId { get; init; }

    internal required string Role { get; init; }

    internal required string Target { get; init; }

    internal required string Status { get; init; }

    internal int? RowCount { get; init; }

    internal int PayloadBytes { get; init; }

    internal long ElapsedMilliseconds { get; init; }
}
