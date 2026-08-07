using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record RecordingSearchResponseDto
{
    [JsonPropertyName("isrc")]
    public string? Isrc { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("recordings")]
    public IReadOnlyList<RecordingDto>? Recordings { get; init; }
}
