using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record MediumDto
{
    [JsonPropertyName("position")]
    public int? Position { get; init; }

    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("track-count")]
    public int? TrackCount { get; init; }

    [JsonPropertyName("tracks")]
    public IReadOnlyList<TrackDto>? Tracks { get; init; }
}
