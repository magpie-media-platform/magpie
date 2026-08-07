using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record ReleaseSearchResponseDto
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("releases")]
    public IReadOnlyList<ReleaseDto>? Releases { get; init; }
}
