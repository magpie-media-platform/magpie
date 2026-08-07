using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record CastCreditDto
{
    [JsonPropertyName("self")]
    public bool Self { get; init; }

    [JsonPropertyName("voice")]
    public bool Voice { get; init; }

    [JsonPropertyName("_embedded")]
    public CreditEmbeddedDto? Embedded { get; init; }
}
