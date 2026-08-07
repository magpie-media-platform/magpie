using System.Collections.Generic;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record FetchPlanNode : PlanNode
{
    internal required QueryTarget Target { get; init; }

    internal required AccessMode AccessMode { get; init; }

    internal required AnchorKind AnchorKind { get; init; }

    internal required CombineMode CombineMode { get; init; }

    internal required IReadOnlyList<QueryFilter> Filters { get; init; }

    internal IReadOnlyList<ResolvePlanNode> Resolvers { get; init; } = [];

    internal PlanNode? AnchorSource { get; init; }

    internal string? DirectValue { get; init; }

    internal string? OrdinaryTerm { get; init; }

    internal LookupCapability LookupCapabilities { get; init; }

    internal EntityKind? EntityKind { get; init; }

    internal ArtistComposition? BrowseComposition { get; init; }

    internal bool UsesReleaseIndexComposition { get; init; }
}
