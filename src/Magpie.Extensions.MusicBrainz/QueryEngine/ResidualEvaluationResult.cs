using System.Collections.Generic;

using Magpie.Extensions.MusicBrainz.Mapping;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record ResidualEvaluationResult
{
    public required bool Accepted { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public SearchHit? Candidate { get; init; }
}
