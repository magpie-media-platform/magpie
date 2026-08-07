namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record ArtistReference
{
    public string? Mbid { get; init; }

    public string? CanonicalName { get; init; }

    public string? CreditName { get; init; }

    public string? ArtistType { get; init; }
}
