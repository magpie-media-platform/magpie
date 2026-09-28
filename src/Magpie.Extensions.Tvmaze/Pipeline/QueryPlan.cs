using System.Collections.Generic;
using System.Text.Json.Serialization;

using Magpie.Models.Data;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed record QueryPlan
{
    [JsonIgnore]
    internal QueryProgress? Progress { get; init; }

    [JsonIgnore]
    public ContentHierarchyItemCriteria Query { get; init; } = null!;

    [JsonIgnore]
    public MetadataCriteria Metadata { get; init; } = null!;

    public required int MaximumResults { get; init; }

    public required PlanNode Root { get; init; }

    public required IReadOnlyList<CriterionDisposition> Dispositions { get; init; }
}
