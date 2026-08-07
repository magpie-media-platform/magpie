using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record WorkSearchResponseDto
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("works")]
    public IReadOnlyList<WorkDto>? Works { get; init; }
}
