using System.Text.Json.Serialization;

namespace Magpie.Extensions.OpenLibrary.Mapping;

internal sealed record AuthorLinkDto
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}
