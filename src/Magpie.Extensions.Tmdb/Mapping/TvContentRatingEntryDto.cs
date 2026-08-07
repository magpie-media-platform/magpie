using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record TvContentRatingEntryDto
{
    [JsonPropertyName("iso_3166_1")]
    public string? CountryCode { get; init; }

    [JsonPropertyName("rating")]
    public string? Rating { get; init; }
}
