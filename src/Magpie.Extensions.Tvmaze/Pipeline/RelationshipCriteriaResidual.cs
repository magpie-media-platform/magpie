using System.Collections.Generic;

using Magpie.Models.Data;

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
