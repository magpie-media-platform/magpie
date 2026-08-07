using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record RatingDto
{
    [JsonPropertyName("average")]
    public double? Average { get; init; }
}
