using System.Collections.Generic;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record ArtistResolutionResponse
{
    public int Count { get; init; }

    public int ReturnedCount { get; init; }

    public IReadOnlyList<ArtistResolutionCandidate> Candidates { get; init; } = [];
}
