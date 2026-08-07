using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record TvAlternativeTitlesDto
{
    [JsonPropertyName("results")]
    public IReadOnlyList<AlternativeTitleEntryDto>? Results { get; init; }
}
