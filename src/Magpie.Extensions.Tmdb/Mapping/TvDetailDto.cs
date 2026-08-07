using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record TvDetailDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("original_name")]
    public string? OriginalName { get; init; }

    [JsonPropertyName("overview")]
    public string? Overview { get; init; }

    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; init; }

    [JsonPropertyName("episode_run_time")]
    public IReadOnlyList<int>? EpisodeRunTime { get; init; }

    [JsonPropertyName("original_language")]
    public string? OriginalLanguage { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("genres")]
    public IReadOnlyList<GenreDto>? Genres { get; init; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; init; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; init; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }

    [JsonPropertyName("external_ids")]
    public ExternalIdsDto? ExternalIds { get; init; }

    [JsonPropertyName("credits")]
    public CreditsDto? Credits { get; init; }

    [JsonPropertyName("alternative_titles")]
    public TvAlternativeTitlesDto? AlternativeTitles { get; init; }

    [JsonPropertyName("content_ratings")]
    public TvContentRatingsDto? ContentRatings { get; init; }

    [JsonPropertyName("videos")]
    public VideosDto? Videos { get; init; }

    [JsonPropertyName("images")]
    public ImagesPayloadDto? Images { get; init; }

    [JsonPropertyName("seasons")]
    public IReadOnlyList<TvSeasonSummaryDto>? Seasons { get; init; }

    [JsonPropertyName("production_companies")]
    public IReadOnlyList<ProductionCompanyDto>? ProductionCompanies { get; init; }

    [JsonPropertyName("networks")]
    public IReadOnlyList<ProductionCompanyDto>? Networks { get; init; }
}
