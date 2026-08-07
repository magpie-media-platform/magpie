using System.Text.Json.Serialization;

namespace Magpie.Extensions.OpenLibrary.Mapping;

internal sealed record AuthorRemoteIdsDto
{
    [JsonPropertyName("wikidata")]
    public string? Wikidata { get; init; }

    [JsonPropertyName("viaf")]
    public string? Viaf { get; init; }

    [JsonPropertyName("isni")]
    public string? Isni { get; init; }

    [JsonPropertyName("goodreads")]
    public string? Goodreads { get; init; }

    [JsonPropertyName("amazon")]
    public string? Amazon { get; init; }
}
