namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record RequestBuildResult
{
    public Request? Request { get; init; }

    public bool TooLarge { get; init; }
}
