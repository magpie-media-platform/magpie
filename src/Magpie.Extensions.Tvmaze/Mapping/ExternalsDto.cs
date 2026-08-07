using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record ExternalsDto
{
    [JsonPropertyName("imdb")]
    public string? Imdb { get; init; }

    [JsonPropertyName("thetvdb")]
    public long? TheTvdb { get; init; }

    [JsonPropertyName("tvrage")]
    public long? TvRage { get; init; }
}
