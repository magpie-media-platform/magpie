using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record SeasonDetailDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("overview")]
    public string? Overview { get; init; }

    [JsonPropertyName("air_date")]
    public string? AirDate { get; init; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; init; }

    [JsonPropertyName("episodes")]
    public IReadOnlyList<EpisodeDto>? Episodes { get; init; }

    [JsonPropertyName("credits")]
    public CreditsDto? Credits { get; init; }

    [JsonPropertyName("external_ids")]
    public ExternalIdsDto? ExternalIds { get; init; }

    [JsonPropertyName("images")]
    public ImagesPayloadDto? Images { get; init; }
}
