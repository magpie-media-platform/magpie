using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record ImageResolutionsDto
{
    [JsonPropertyName("original")]
    public ImageResolutionDto? Original { get; init; }

    [JsonPropertyName("medium")]
    public ImageResolutionDto? Medium { get; init; }
}
