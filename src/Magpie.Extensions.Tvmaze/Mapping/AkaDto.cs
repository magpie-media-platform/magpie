using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record AkaDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
