using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record CrewEntryDto
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("person")]
    public PersonDto? Person { get; init; }
}
