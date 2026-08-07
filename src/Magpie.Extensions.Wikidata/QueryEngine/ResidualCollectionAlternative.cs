using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record ResidualCollectionAlternative
{
    public IReadOnlyList<ResidualCollectionPredicate> Predicates { get; init; } = [];
}
