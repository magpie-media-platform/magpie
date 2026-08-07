using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record AliasDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("locale")]
    public string? Locale { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}
