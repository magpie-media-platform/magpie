using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record VideoDto
{
    [JsonPropertyName("iso_639_1")]
    public string? Iso6391 { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("site")]
    public string? Site { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("official")]
    public bool Official { get; init; }

    [JsonPropertyName("published_at")]
    public string? PublishedAt { get; init; }
}
