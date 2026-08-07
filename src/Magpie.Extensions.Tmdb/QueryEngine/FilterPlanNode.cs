using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record FilterPlanNode : PlanNode
{
    internal required ContentRoute Route { get; init; }

    internal required PlanNode Child { get; init; }

    internal required IReadOnlyList<string> ResidualPredicates { get; init; }

    internal required IReadOnlyList<TextFilter> TextFilters { get; init; }

    internal required IReadOnlyList<TemporalFilter> PublishDateFilters { get; init; }

    internal required IReadOnlyList<DurationFilter> DurationFilters { get; init; }

    internal required IReadOnlyList<GenreFilter> GenreFilters { get; init; }

    internal required IReadOnlyList<SubjectResolutionBinding> SubjectBindings { get; init; }

    internal required IReadOnlyList<ContentIdFilter> ContentIdFilters { get; init; }

    internal required IReadOnlyList<ElementFilter> ElementFilters { get; init; }

    internal required int RequestedPage { get; init; }

    internal int? MaximumResults { get; init; }
}
