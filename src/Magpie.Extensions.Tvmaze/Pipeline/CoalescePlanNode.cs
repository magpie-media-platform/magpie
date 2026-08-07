using System.Collections.Generic;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed record CoalescePlanNode : PlanNode
{
    public required IReadOnlyList<PlanNode> Children { get; init; }
}
