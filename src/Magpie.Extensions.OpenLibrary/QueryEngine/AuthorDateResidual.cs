using System.Collections.Generic;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record AuthorDateResidual
{
    public required AuthorDateField Field { get; init; }

    public required CriteriaValueOperator Operator { get; init; }

    public bool Negate { get; init; }

    public IReadOnlyList<TemporalValue> TemporalValues { get; init; } = [];
}
