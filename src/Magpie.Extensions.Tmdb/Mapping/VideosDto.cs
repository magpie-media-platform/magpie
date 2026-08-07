using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record VideosDto
{
    [JsonPropertyName("results")]
    public IReadOnlyList<VideoDto>? Results { get; init; }
}
