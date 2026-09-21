using System.Collections.Generic;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal abstract record SearchPlanNode
{
    internal sealed record Seeded(
        SearchSeed Seed,
        string SeedReason,
        IReadOnlyList<LocalFilter> HitFilters,
        IReadOnlyList<LocalFilter> AkaFilters,
        IReadOnlyList<RelationshipCriteriaResidual> RelationshipResiduals,
        IReadOnlyList<ImageCriteriaResidual> ImageResiduals,
        IReadOnlyList<CriterionDisposition> Dispositions) : SearchPlanNode;

    internal sealed record KnownEmpty(IReadOnlyList<CriterionDisposition> Dispositions) : SearchPlanNode;

    internal sealed record Browse(
        IReadOnlyList<LocalFilter> HitFilters,
        IReadOnlyList<LocalFilter> AkaFilters,
        IReadOnlyList<RelationshipCriteriaResidual> RelationshipResiduals,
        IReadOnlyList<ImageCriteriaResidual> ImageResiduals,
        IReadOnlyList<CriterionDisposition> Dispositions) : SearchPlanNode;
}
