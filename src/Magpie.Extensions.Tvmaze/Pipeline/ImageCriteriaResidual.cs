using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed record ImageCriteriaResidual(bool Negate, IReadOnlyList<ImageCriteriaResidual.Alternative> Alternatives)
{
    internal enum EvaluationKind
    {
        Presence,
        Gallery,
        PassThrough
    }

    internal sealed record Alternative(ImageCriteria Criteria, EvaluationKind Kind);
}
