using System.Collections.Generic;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record QueryPlan
{
    public required PlanNode Root { get; init; }

    public required BookHierarchy Hierarchy { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<string> CandidateLanguagePreferences { get; init; } = [];

    public IReadOnlyList<string> CriterionDispositions { get; init; } = [];

    public string AnchorKind { get; init; } = "none";

    public int Page { get; init; } = 1;

    public int MaxResults { get; init; } = 20;

    public bool Advanced { get; init; }

    public string? ResolutionSource { get; init; }

    public string? ResolutionValue { get; init; }
}
