namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record IdentityValue
{
    public required string Source { get; init; }

    public required string Value { get; init; }

    public required string RemoteField { get; init; }
}
