using Magpie.Extensions.MusicBrainz.QueryEngine;
using Magpie.Models.Enums;

namespace Magpie.Extensions.MusicBrainz;

internal sealed record ArtistResolutionCacheKey
{
    public required ArtistCompositionEntity Entity { get; init; }

    public required string Name { get; init; }

    public CriteriaValueOperator NameOperator { get; init; }

    public string? RelatedMbid { get; init; }

    public int Offset { get; init; }
}
