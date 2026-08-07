using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record CompanyDetailDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("headquarters")]
    public string? Headquarters { get; init; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }

    [JsonPropertyName("origin_country")]
    public string? OriginCountry { get; init; }

    [JsonPropertyName("logo_path")]
    public string? LogoPath { get; init; }

    [JsonPropertyName("parent_company")]
    public ParentCompanyDto? ParentCompany { get; init; }
}
