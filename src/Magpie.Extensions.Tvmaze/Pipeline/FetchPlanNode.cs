namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed record FetchPlanNode : PlanNode
{
    public required SearchAnchorKind AnchorKind { get; init; }

    public required SearchSeed Seed { get; init; }

    public ResolvePlanNode? Resolve { get; init; }
}
