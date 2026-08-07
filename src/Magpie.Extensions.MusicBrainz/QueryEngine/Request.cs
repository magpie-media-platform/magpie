namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record Request
{
    public required string RelativeTarget { get; init; }

    public required QueryTarget Target { get; init; }

    public required string Fingerprint { get; init; }

    public required int Limit { get; init; }

    public required int Offset { get; init; }

    public bool ReleaseIndex { get; init; }
}
