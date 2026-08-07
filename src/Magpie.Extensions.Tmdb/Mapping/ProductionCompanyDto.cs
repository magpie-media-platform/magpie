using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record ProductionCompanyDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("logo_path")]
    public string? LogoPath { get; init; }

    [JsonPropertyName("origin_country")]
    public string? OriginCountry { get; init; }
}
