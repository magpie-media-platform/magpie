using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Pipeline;

[JsonDerivedType(typeof(ResolvePlanNode), "resolve")]
[JsonDerivedType(typeof(FetchPlanNode), "fetch")]
[JsonDerivedType(typeof(UnionPlanNode), "union")]
[JsonDerivedType(typeof(FilterPlanNode), "filter")]
[JsonDerivedType(typeof(CoalescePlanNode), "coalesce")]
[JsonDerivedType(typeof(IntersectPlanNode), "intersect")]
internal abstract record PlanNode
{
    public required int Id { get; init; }

    public required string Contributes { get; init; }
}
