namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record PlanPrecondition
{
    internal required PlanPreconditionKind Kind { get; init; }

    internal int? NodeId { get; init; }

    internal static PlanPrecondition None { get; } = new()
    {
        Kind = PlanPreconditionKind.None
    };
}
