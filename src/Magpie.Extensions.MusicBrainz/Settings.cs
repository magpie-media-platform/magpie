namespace Magpie.Extensions.MusicBrainz;

internal sealed record Settings
{
    public const string ContactKey = "contact";

    public const string GroupName = "magpie.musicbrainz";

    public string? Contact { get; init; }
}
