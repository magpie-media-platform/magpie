using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record FindEnvelopeDto
{
    [JsonPropertyName("movie_results")]
    public IReadOnlyList<MovieSearchHitDto>? MovieResults { get; init; }

    [JsonPropertyName("tv_results")]
    public IReadOnlyList<TvSearchHitDto>? TvResults { get; init; }

    [JsonPropertyName("person_results")]
    public IReadOnlyList<NamedSearchHitDto>? PersonResults { get; init; }
}
