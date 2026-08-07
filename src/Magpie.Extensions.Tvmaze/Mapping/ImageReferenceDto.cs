using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record ImageReferenceDto
{
    [JsonPropertyName("medium")]
    public string? Medium { get; init; }

    [JsonPropertyName("original")]
    public string? Original { get; init; }
}
