namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record ResolvePlanNode : PlanNode
{
    internal required ArtistComposition Composition { get; init; }

    internal int? RelationshipFilterIndex { get; init; }

    internal int? RelationshipValueIndex { get; init; }
}
