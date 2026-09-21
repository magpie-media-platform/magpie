using System.Collections.Generic;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record PlannedCriterion
{
    public IReadOnlyList<QueryFilter> Filters { get; init; } = [];

    public IReadOnlyList<SourceTerm> SourceTerms { get; init; } = [];

    public IReadOnlyList<SourceGroup> SourceGroups { get; init; } = [];

    public IReadOnlyList<FilterGroup> FilterGroups { get; init; } = [];

    public IReadOnlyList<AuthorDateResidual> Residuals { get; init; } = [];

    public bool NeedsIndependentSeed { get; init; }

    public bool RecallBoundedSource { get; init; }

    public bool MatchesNothing { get; init; }

    public bool CannotFulfill { get; init; }
}
