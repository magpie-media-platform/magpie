namespace Magpie.Extensions.MusicBrainz;

internal sealed record CoverCacheKey
{
    public required string ReleaseGroupMbid { get; init; }

    public required int ThumbnailSize { get; init; }
}
