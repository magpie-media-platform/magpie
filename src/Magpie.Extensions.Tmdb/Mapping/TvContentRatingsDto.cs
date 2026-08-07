using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record TvContentRatingsDto
{
    [JsonPropertyName("results")]
    public IReadOnlyList<TvContentRatingEntryDto>? Results { get; init; }
}
