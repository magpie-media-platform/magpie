namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record ArtistResolutionRequest
{
    public required ArtistCompositionEntity Entity { get; init; }

    public required string RelativeTarget { get; init; }

    public required string Fingerprint { get; init; }
}
