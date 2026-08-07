namespace Magpie.Extensions.Tmdb.QueryEngine;

internal abstract record PlanNode
{
    internal required int Id { get; init; }

    internal required PlanPrecondition Precondition { get; init; }

    internal required string Contributes { get; init; }
}
