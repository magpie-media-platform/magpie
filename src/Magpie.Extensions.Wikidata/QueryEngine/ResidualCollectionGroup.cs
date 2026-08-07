using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record ResidualCollectionGroup
{
    public required ResidualCollectionKind Kind { get; init; }

    public bool OuterNegate { get; init; }

    public required IReadOnlyList<ResidualCollectionAlternative> Alternatives { get; init; }
}
