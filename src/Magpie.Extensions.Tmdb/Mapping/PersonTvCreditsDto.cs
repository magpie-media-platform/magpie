using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record PersonTvCreditsDto
{
    [JsonPropertyName("cast")]
    public IReadOnlyList<TvCreditDto>? Cast { get; init; }

    [JsonPropertyName("crew")]
    public IReadOnlyList<TvCreditDto>? Crew { get; init; }
}
