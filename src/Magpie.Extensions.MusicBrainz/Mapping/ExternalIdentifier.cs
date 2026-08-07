namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record ExternalIdentifier
{
    public required string Source { get; init; }

    public required string Value { get; init; }
}
