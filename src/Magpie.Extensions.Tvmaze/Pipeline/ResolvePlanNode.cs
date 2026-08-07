namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed record ResolvePlanNode : PlanNode
{
    public required SearchSeed.PersonChain Request { get; init; }
}
