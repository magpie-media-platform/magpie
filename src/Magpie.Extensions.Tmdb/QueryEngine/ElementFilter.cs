using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record ElementFilter
{
    internal required ElementFamily Family { get; init; }

    internal required bool Excluded { get; init; }

    internal required IReadOnlyList<IReadOnlyList<ElementPredicate>> Alternatives { get; init; }
}
