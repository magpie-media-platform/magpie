using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record ArtistSearchResponseDto
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("artists")]
    public IReadOnlyList<ArtistDto>? Artists { get; init; }
}
