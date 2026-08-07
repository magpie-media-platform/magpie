using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record DiscoverSource
{
    internal required IReadOnlyList<ResolvedSubjectGroup> Subjects { get; init; }

    internal required IReadOnlyDictionary<GenreFilter, IReadOnlyList<int>> GenreSelections { get; init; }
}
