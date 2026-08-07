using System.Collections.Generic;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed record IntersectPlanNode : PlanNode
{
    public required IReadOnlyList<PlanNode> Children { get; init; }
}
