using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record ResidualCollectionPredicate
{
    public required ResidualCollectionField Field { get; init; }

    public required CriteriaValueOperator Operator { get; init; }

    public bool Negate { get; init; }

    public IReadOnlyList<string> StringValues { get; init; } = [];

    public IReadOnlyList<string> EntityQids { get; init; } = [];

    public IReadOnlyList<int> IntegerValues { get; init; } = [];
}
