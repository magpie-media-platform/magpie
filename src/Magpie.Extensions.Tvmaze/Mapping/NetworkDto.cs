using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record NetworkDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("country")]
    public CountryDto? Country { get; init; }

    [JsonPropertyName("officialSite")]
    public string? OfficialSite { get; init; }
}
