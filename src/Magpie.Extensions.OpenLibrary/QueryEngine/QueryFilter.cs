using System.Collections.Generic;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record QueryFilter
{
    public required QueryField Field { get; init; }

    public required CriteriaValueOperator Operator { get; init; }

    public IReadOnlyList<string> Values { get; init; } = [];

    public IReadOnlyList<TemporalValue> TemporalValues { get; init; } = [];

    public bool Negate { get; init; }
}
