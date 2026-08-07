using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record QueryPlan
{
    public string Fingerprint { get; init; } = string.Empty;

    public required EntityKind EntityKind { get; init; }

    public required QueryStrategy Strategy { get; init; }

    public required IReadOnlyList<QueryFilter> Filters { get; init; }

    public IReadOnlyList<ResidualFilter> ResidualFilters { get; init; } = [];

    public IReadOnlyList<CriterionDisposition> CriterionDispositions { get; init; } = [];

    public required IReadOnlyList<string> Warnings { get; init; }

    public required int Page { get; init; }

    public int MaximumResults { get; init; } = SparqlQueryBuilder.PageSize;

    public bool UsesLocalPaging { get; init; }

    public string? OrdinaryTerm { get; init; }

    public IReadOnlyList<string> TitleCandidateQids { get; init; } = [];

    public bool ProjectCandidateDetails { get; init; }

    public string Boundedness { get; init; } = string.Empty;
}
