using System;
using System.Collections.Generic;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record QueryFilter
{
    public string? CriterionSource { get; init; }

    public required QueryFilterKind Kind { get; init; }

    public CriteriaValueOperator Operator { get; init; } = CriteriaValueOperator.Equals;

    public bool Negate { get; init; }

    public string? PropertyId { get; init; }

    public IReadOnlyList<string> Values { get; init; } = [];

    public IReadOnlyList<string> LanguageQids { get; init; } = [];

    public IReadOnlyList<TemporalValue> TemporalValues { get; init; } = [];

    public IReadOnlyList<TimeSpan> DurationValues { get; init; } = [];

    public IReadOnlyList<SubjectFilter> Alternatives { get; init; } = [];
}
