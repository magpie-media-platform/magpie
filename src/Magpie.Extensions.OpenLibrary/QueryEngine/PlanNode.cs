using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record PlanNode
{
    public required PlanNodeKind Kind { get; init; }

    public PlanNodeState State { get; init; } = PlanNodeState.Ready;

    public string? Reason { get; init; }

    public IReadOnlyList<PlanNode> Children { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyList<SourceTerm> SourceTerms { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyList<SourceGroup> SourceGroups { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyList<QueryFilter> Filters { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyList<FilterGroup> FilterGroups { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyList<AuthorDateResidual> Residuals { get; init; } = [];

    public IReadOnlyList<string> Predicates { get; init; } = [];

    public IReadOnlyList<string> SourceFields { get; init; } = [];

    public IReadOnlyList<string> RequiredFields { get; init; } = [];

    public string LanguagePreference { get; init; } = string.Empty;

    public FetchPagingMode PagingMode { get; init; }
}
