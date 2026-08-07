using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record PersonMovieCreditsDto
{
    [JsonPropertyName("cast")]
    public IReadOnlyList<MovieCreditDto>? Cast { get; init; }

    [JsonPropertyName("crew")]
    public IReadOnlyList<MovieCreditDto>? Crew { get; init; }
}
