using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record DiscoverParameter
{
    internal required string Name { get; init; }

    internal required DiscoverCombination Combination { get; init; }

    internal IReadOnlyList<string> SubjectBindingKeys { get; init; } = [];

    internal IReadOnlyList<GenreFilter> GenreFilters { get; init; } = [];
}
