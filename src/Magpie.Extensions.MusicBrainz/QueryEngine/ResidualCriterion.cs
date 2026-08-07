using System.Collections.Generic;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record ResidualCriterion
{
    public required ResidualKind Kind { get; init; }

    public required ResidualNeed Need { get; init; }

    public bool Negate { get; init; }

    public bool IncludePrimaryTitle { get; init; }

    public IReadOnlyList<ResidualAlternative> Alternatives { get; init; } = [];
}
