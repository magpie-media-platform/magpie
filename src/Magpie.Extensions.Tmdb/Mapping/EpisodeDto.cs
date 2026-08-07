using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record EpisodeDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("episode_number")]
    public int EpisodeNumber { get; init; }

    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("overview")]
    public string? Overview { get; init; }

    [JsonPropertyName("air_date")]
    public string? AirDate { get; init; }

    [JsonPropertyName("runtime")]
    public int? Runtime { get; init; }

    [JsonPropertyName("still_path")]
    public string? StillPath { get; init; }

    [JsonPropertyName("credits")]
    public CreditsDto? Credits { get; init; }

    [JsonPropertyName("external_ids")]
    public ExternalIdsDto? ExternalIds { get; init; }

    [JsonPropertyName("images")]
    public ImagesPayloadDto? Images { get; init; }

    [JsonPropertyName("crew")]
    public IReadOnlyList<CrewMemberDto>? Crew { get; init; }

    [JsonPropertyName("guest_stars")]
    public IReadOnlyList<CastMemberDto>? GuestStars { get; init; }
}
