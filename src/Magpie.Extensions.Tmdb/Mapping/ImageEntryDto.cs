using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record ImageEntryDto
{
    [JsonPropertyName("file_path")]
    public string? FilePath { get; init; }

    [JsonPropertyName("iso_639_1")]
    public string? LanguageCode { get; init; }

    [JsonPropertyName("width")]
    public int? Width { get; init; }

    [JsonPropertyName("height")]
    public int? Height { get; init; }
}
