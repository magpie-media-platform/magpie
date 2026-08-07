using System.Collections.Generic;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record IntersectPlanNode : PlanNode
{
    internal required IReadOnlyList<PlanNode> Children { get; init; }
}
