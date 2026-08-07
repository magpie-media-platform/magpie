namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record PlanPrecondition
{
    internal required PlanPreconditionKind Kind { get; init; }

    internal int? NodeId { get; init; }
}
