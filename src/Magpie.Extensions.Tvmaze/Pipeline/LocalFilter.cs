using System;
using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed record LocalFilter
{
    public required LocalFilterKind Kind { get; init; }

    public CriteriaValueOperator Operator { get; init; } = CriteriaValueOperator.Equals;

    public bool Negate { get; init; }

    public IReadOnlyList<string> StringValues { get; init; } = [];

    public IReadOnlyList<TemporalValue> TemporalValues { get; init; } = [];

    public IReadOnlyList<TimeSpan> DurationValues { get; init; } = [];

    public IReadOnlyList<GenreCriteria> GenreAlternatives { get; init; } = [];

    public IReadOnlyList<ExternalIdCriteria> ExternalIdAlternatives { get; init; } = [];

    public IReadOnlyList<AlternateTextCriteria> AlternateTextAlternatives { get; init; } = [];

    public IReadOnlyList<ReferenceMetadataCriteria> ReferenceAlternatives { get; init; } = [];
}
