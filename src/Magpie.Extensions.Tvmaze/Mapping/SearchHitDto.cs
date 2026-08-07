using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record SearchHitDto
{
    [JsonPropertyName("score")]
    public double Score { get; init; }

    [JsonPropertyName("show")]
    public ShowDto? Show { get; init; }
}
