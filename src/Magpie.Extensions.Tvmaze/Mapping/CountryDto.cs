using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record CountryDto
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }
}
