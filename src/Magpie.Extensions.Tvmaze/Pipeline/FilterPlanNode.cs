using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed record FilterPlanNode : PlanNode
{
    public required PlanNode Child { get; init; }

    [JsonIgnore]
    public IReadOnlyList<LocalFilter> HitFilters { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyList<LocalFilter> AkaFilters { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyList<RelationshipCriteriaResidual> RelationshipResiduals { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyList<ImageCriteriaResidual> ImageResiduals { get; init; } = [];

    public int HitFilterCount => HitFilters.Count;

    public int AkaFilterCount => AkaFilters.Count;

    public int RelationshipResidualCount => RelationshipResiduals.Count;

    public int ImageResidualCount => ImageResiduals.Count;
}
