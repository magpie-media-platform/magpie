using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record LabelInfoDto
{
    [JsonPropertyName("catalog-number")]
    public string? CatalogNumber { get; init; }

    [JsonPropertyName("label")]
    public LabelDto? Label { get; init; }
}
