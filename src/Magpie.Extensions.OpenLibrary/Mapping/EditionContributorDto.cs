using System.Text.Json.Serialization;

namespace Magpie.Extensions.OpenLibrary.Mapping;

internal sealed record EditionContributorDto
{
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
