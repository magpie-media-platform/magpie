using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record CoalescePlanNode : PlanNode
{
    internal required IReadOnlyList<PlanNode> Children { get; init; }
}
