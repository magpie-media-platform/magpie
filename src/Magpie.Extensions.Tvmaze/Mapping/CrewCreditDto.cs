using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record CrewCreditDto
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("_embedded")]
    public CreditEmbeddedDto? Embedded { get; init; }
}
