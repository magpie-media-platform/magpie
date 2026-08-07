using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record SeasonDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("number")]
    public int? Number { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("episodeOrder")]
    public int? EpisodeOrder { get; init; }

    [JsonPropertyName("premiereDate")]
    public string? PremiereDate { get; init; }

    [JsonPropertyName("endDate")]
    public string? EndDate { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("image")]
    public ImageReferenceDto? Image { get; init; }
}
