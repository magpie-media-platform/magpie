using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record ReleaseGroupSearchResponseDto
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("release-group-count")]
    public int? ReleaseGroupCount { get; init; }

    [JsonPropertyName("release-group-offset")]
    public int? ReleaseGroupOffset { get; init; }

    [JsonPropertyName("release-groups")]
    public IReadOnlyList<ReleaseGroupDto>? ReleaseGroups { get; init; }
}
