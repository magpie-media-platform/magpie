namespace Magpie.Extensions.MusicBrainz;

internal sealed record BrowseCacheKey
{
    public required string ArtistMbid { get; init; }

    public required string StatusFilter { get; init; }

    public required int Limit { get; init; }

    public required int Offset { get; init; }
}
