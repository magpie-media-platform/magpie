using System.Collections.Generic;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record FilterPlanNode : PlanNode
{
    internal required PlanNode Child { get; init; }

    internal required IReadOnlyList<QueryFilter> Filters { get; init; }

    internal required IReadOnlyList<ResidualCriterion> Residuals { get; init; }

    internal IReadOnlyList<PlanNode> Exclusions { get; init; } = [];
}
