using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record PersonDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("country")]
    public CountryDto? Country { get; init; }

    [JsonPropertyName("birthday")]
    public string? Birthday { get; init; }

    [JsonPropertyName("deathday")]
    public string? Deathday { get; init; }

    [JsonPropertyName("gender")]
    public string? Gender { get; init; }

    [JsonPropertyName("image")]
    public ImageReferenceDto? Image { get; init; }
}
