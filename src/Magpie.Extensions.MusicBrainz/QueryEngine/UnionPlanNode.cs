using System.Collections.Generic;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record UnionPlanNode : PlanNode
{
    internal required IReadOnlyList<PlanNode> Children { get; init; }
}
