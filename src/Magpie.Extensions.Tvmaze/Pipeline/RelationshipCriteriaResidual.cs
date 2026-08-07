using System.Collections.Generic;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed record RelationshipCriteriaResidual(
    int? SourceGroupIndex,
    bool Negate,
    IReadOnlyList<RelationshipCriteriaResidual.Alternative> Alternatives)
{
    internal sealed record Alternative(
        RelationshipCriteria Criteria,
        bool MatchesNetwork,
        bool MatchesPerson,
        bool PassThrough);
}
