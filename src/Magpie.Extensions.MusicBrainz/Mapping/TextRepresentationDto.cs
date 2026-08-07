using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record TextRepresentationDto
{
    [JsonPropertyName("language")]
    public string? Language { get; init; }
}
