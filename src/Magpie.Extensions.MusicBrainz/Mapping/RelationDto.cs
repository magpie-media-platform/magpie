using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record RelationDto
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("url")]
    public UrlDto? Url { get; init; }

    [JsonPropertyName("target-type")]
    public string? TargetType { get; init; }

    [JsonPropertyName("direction")]
    public string? Direction { get; init; }

    [JsonPropertyName("artist")]
    public ArtistDto? Artist { get; init; }

    [JsonPropertyName("work")]
    public WorkDto? Work { get; init; }

    [JsonPropertyName("attributes")]
    public IReadOnlyList<string>? Attributes { get; init; }

    [JsonPropertyName("attribute-values")]
    public IReadOnlyDictionary<string, string>? AttributeValues { get; init; }

    [JsonPropertyName("attribute-credits")]
    public IReadOnlyDictionary<string, string>? AttributeCredits { get; init; }
}
