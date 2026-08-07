namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record LabelReference
{
    public string? Mbid { get; init; }

    public string? Name { get; init; }
}
