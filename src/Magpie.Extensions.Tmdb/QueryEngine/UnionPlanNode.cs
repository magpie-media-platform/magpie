using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record UnionPlanNode : PlanNode
{
    internal required IReadOnlyList<PlanNode> Children { get; init; }

    internal bool ExpandsResolvedSubjectIds { get; init; }
}
