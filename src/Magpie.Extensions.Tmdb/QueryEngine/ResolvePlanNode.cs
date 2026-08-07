using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record ResolvePlanNode : PlanNode
{
    internal required ContentRoute Route { get; init; }

    internal required IReadOnlyList<SubjectResolutionBinding> SubjectBindings { get; init; }

    internal required IReadOnlyList<GenreFilter> GenreFilters { get; init; }

    internal required bool ResolveImageBase { get; init; }
}
