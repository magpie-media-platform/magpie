using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record DiscoverRequest
{
    internal required Request Request { get; init; }

    internal required IReadOnlyCollection<TextFilter> AppliedTextFilters { get; init; }

    internal required IReadOnlyCollection<TemporalFilter> AppliedPublishDateFilters { get; init; }

    internal required IReadOnlyCollection<DurationFilter> AppliedDurationFilters { get; init; }

    internal required IReadOnlyCollection<GenreFilter> AppliedGenreFilters { get; init; }

    internal required IReadOnlyCollection<ResolvedSubjectGroup> AppliedSubjectGroups { get; init; }

    internal IReadOnlyCollection<ElementFilter> AppliedElementFilters { get; init; } = [];
}
