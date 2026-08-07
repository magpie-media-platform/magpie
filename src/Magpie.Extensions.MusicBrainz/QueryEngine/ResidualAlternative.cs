using System.Collections.Generic;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record ResidualAlternative
{
    public IReadOnlyList<ResidualPredicate> Predicates { get; init; } = [];
}
