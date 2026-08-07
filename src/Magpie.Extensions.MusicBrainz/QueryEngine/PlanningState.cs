using System.Collections.Generic;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record PlanningState
{
    internal required QueryTarget Target { get; init; }

    internal required IReadOnlyList<QueryFilter> Filters { get; init; }

    internal required IReadOnlyList<ResidualCriterion> Residuals { get; init; }

    internal required IReadOnlyList<ArtistComposition> ArtistCompositions { get; init; }

    internal string? DirectValue { get; init; }

    internal string? OrdinaryTerm { get; init; }

    internal bool UsesReleaseIndexComposition { get; init; }
}
