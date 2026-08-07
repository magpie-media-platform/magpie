using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record GenreDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }
}
