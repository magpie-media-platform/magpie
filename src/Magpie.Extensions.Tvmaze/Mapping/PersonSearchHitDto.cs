using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record PersonSearchHitDto
{
    [JsonPropertyName("score")]
    public double Score { get; init; }

    [JsonPropertyName("person")]
    public PersonDto? Person { get; init; }
}
