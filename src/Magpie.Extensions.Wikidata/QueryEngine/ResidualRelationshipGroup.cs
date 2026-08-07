using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record ResidualRelationshipGroup
{
    public required bool OuterNegate { get; init; }

    public required IReadOnlyList<ResidualRelationshipAlternative> Alternatives { get; init; }
}
