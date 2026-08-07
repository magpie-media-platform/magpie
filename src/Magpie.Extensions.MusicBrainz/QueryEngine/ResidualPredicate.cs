using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record ResidualPredicate
{
    public required ResidualField Field { get; init; }

    public required CriteriaValueOperator Operator { get; init; }

    public bool Negate { get; init; }

    public IReadOnlyList<string> Values { get; init; } = [];

    public IReadOnlyList<long> DurationMilliseconds { get; init; } = [];
}
