using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record IntersectPlanNode : PlanNode
{
    internal required IReadOnlyList<PlanNode> Children { get; init; }
}
