using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record ReleaseDateEntryDto
{
    [JsonPropertyName("certification")]
    public string? Certification { get; init; }

    [JsonPropertyName("type")]
    public int Type { get; init; }
}
