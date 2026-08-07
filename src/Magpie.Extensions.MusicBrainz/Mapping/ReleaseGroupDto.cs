using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record ReleaseGroupDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("first-release-date")]
    public string? FirstReleaseDate { get; init; }

    [JsonPropertyName("primary-type")]
    public string? PrimaryType { get; init; }

    [JsonPropertyName("secondary-types")]
    public IReadOnlyList<string>? SecondaryTypes { get; init; }

    [JsonPropertyName("score")]
    public int? Score { get; init; }

    [JsonPropertyName("disambiguation")]
    public string? Disambiguation { get; init; }

    [JsonPropertyName("artist-credit")]
    public IReadOnlyList<ArtistCreditDto>? ArtistCredit { get; init; }

    [JsonPropertyName("aliases")]
    public IReadOnlyList<AliasDto>? Aliases { get; init; }

    [JsonPropertyName("releases")]
    public IReadOnlyList<ReleaseDto>? Releases { get; init; }

    [JsonPropertyName("genres")]
    public IReadOnlyList<GenreDto>? Genres { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<GenreDto>? Tags { get; init; }

    [JsonPropertyName("relations")]
    public IReadOnlyList<RelationDto>? Relations { get; init; }
}
