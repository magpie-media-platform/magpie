using System.Collections.Generic;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record ArtistResolutionCandidate
{
    public required string Mbid { get; init; }

    public required string Name { get; init; }

    public IReadOnlyList<string> Aliases { get; init; } = [];

    public string? ArtistType { get; init; }
}
