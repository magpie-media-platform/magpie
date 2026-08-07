using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record UrlDto
{
    [JsonPropertyName("resource")]
    public string? Resource { get; init; }
}
