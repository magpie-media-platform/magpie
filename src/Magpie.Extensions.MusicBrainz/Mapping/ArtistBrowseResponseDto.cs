using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record ArtistBrowseResponseDto
{
    [JsonPropertyName("artist-count")]
    public int ArtistCount { get; init; }

    [JsonPropertyName("artists")]
    public IReadOnlyList<ArtistDto>? Artists { get; init; }
}
