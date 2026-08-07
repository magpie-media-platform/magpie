using Magpie.Extensions.MusicBrainz.QueryEngine;

namespace Magpie.Extensions.MusicBrainz;

internal sealed record EntityIdentityKey
{
    public required EntityKind Kind { get; init; }

    public required string Mbid { get; init; }
}
