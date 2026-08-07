using Magpie.Extensions.MusicBrainz.QueryEngine;

namespace Magpie.Extensions.MusicBrainz;

internal sealed record SearchCacheKey
{
    public required QueryTarget Target { get; init; }

    public required string Fingerprint { get; init; }

    public required int Limit { get; init; }

    public required int Offset { get; init; }
}
