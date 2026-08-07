using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record LifeSpanDto
{
    [JsonPropertyName("begin")]
    public string? Begin { get; init; }

    [JsonPropertyName("end")]
    public string? End { get; init; }

    [JsonPropertyName("ended")]
    public bool? Ended { get; init; }
}
