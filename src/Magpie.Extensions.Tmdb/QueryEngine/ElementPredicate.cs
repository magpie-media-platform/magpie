using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record ElementPredicate
{
    internal required ElementField Field { get; init; }

    internal required CriteriaValueOperator Operator { get; init; }

    internal bool Negate { get; init; }

    internal IReadOnlyList<string> StringValues { get; init; } = [];

    internal IReadOnlyList<DateTime> DateValues { get; init; } = [];

    internal IReadOnlyList<int> NumberValues { get; init; } = [];
}
