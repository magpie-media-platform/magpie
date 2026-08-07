using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record CastEntryDto
{
    [JsonPropertyName("person")]
    public PersonDto? Person { get; init; }

    [JsonPropertyName("character")]
    public CharacterDto? Character { get; init; }

    [JsonPropertyName("self")]
    public bool Self { get; init; }

    [JsonPropertyName("voice")]
    public bool Voice { get; init; }
}
