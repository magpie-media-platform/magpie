using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record ShowDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonPropertyName("genres")]
    public IReadOnlyList<string>? Genres { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("runtime")]
    public int? Runtime { get; init; }

    [JsonPropertyName("averageRuntime")]
    public int? AverageRuntime { get; init; }

    [JsonPropertyName("premiered")]
    public string? Premiered { get; init; }

    [JsonPropertyName("ended")]
    public string? Ended { get; init; }

    [JsonPropertyName("officialSite")]
    public string? OfficialSite { get; init; }

    [JsonPropertyName("schedule")]
    public ScheduleDto? Schedule { get; init; }

    [JsonPropertyName("rating")]
    public RatingDto? Rating { get; init; }

    [JsonPropertyName("network")]
    public NetworkDto? Network { get; init; }

    [JsonPropertyName("webChannel")]
    public NetworkDto? WebChannel { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("image")]
    public ImageReferenceDto? Image { get; init; }

    [JsonPropertyName("externals")]
    public ExternalsDto? Externals { get; init; }

    [JsonPropertyName("_embedded")]
    public ShowEmbeddedDto? Embedded { get; init; }
}
