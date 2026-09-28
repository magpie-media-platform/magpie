namespace Magpie.Extensions.MusicBrainz;

internal sealed record Settings
{
    public const string ContactKey = "contact";

    public const string GroupName = "magpie.musicbrainz";

    internal bool FreshSearch { get; init; }

    public string? Contact { get; init; }
}
