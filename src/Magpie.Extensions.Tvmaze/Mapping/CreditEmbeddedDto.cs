using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record CreditEmbeddedDto
{
    [JsonPropertyName("show")]
    public ShowDto? Show { get; init; }
}
