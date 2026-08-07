using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record ExternalIdsDto
{
    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; init; }

    [JsonPropertyName("wikidata_id")]
    public string? WikidataId { get; init; }

    [JsonPropertyName("tvdb_id")]
    public int? TvdbId { get; init; }

    [JsonPropertyName("facebook_id")]
    public string? FacebookId { get; init; }

    [JsonPropertyName("instagram_id")]
    public string? InstagramId { get; init; }

    [JsonPropertyName("twitter_id")]
    public string? TwitterId { get; init; }

    [JsonPropertyName("tiktok_id")]
    public string? TiktokId { get; init; }

    [JsonPropertyName("youtube_id")]
    public string? YoutubeId { get; init; }
}
