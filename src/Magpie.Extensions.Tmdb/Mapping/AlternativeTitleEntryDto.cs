using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record AlternativeTitleEntryDto
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}
