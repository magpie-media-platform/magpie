using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record ImageDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("main")]
    public bool Main { get; init; }

    [JsonPropertyName("resolutions")]
    public ImageResolutionsDto? Resolutions { get; init; }
}
