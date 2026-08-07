using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record MovieDetailDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("original_title")]
    public string? OriginalTitle { get; init; }

    [JsonPropertyName("overview")]
    public string? Overview { get; init; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; init; }

    [JsonPropertyName("runtime")]
    public int? Runtime { get; init; }

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
    public MovieAlternativeTitlesDto? AlternativeTitles { get; init; }

    [JsonPropertyName("release_dates")]
    public MovieReleaseDatesDto? ReleaseDates { get; init; }

    [JsonPropertyName("videos")]
    public VideosDto? Videos { get; init; }

    [JsonPropertyName("images")]
    public ImagesPayloadDto? Images { get; init; }

    [JsonPropertyName("production_companies")]
    public IReadOnlyList<ProductionCompanyDto>? ProductionCompanies { get; init; }
}
