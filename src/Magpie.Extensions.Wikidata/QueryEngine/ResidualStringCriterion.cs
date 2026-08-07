using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record ResidualStringCriterion
{
    public required CriteriaValueOperator Operator { get; init; }

    public required bool Negate { get; init; }

    public required IReadOnlyList<string> Values { get; init; }
}
