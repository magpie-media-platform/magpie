using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record RecordingDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("length")]
    public long? Length { get; init; }

    [JsonPropertyName("disambiguation")]
    public string? Disambiguation { get; init; }

    [JsonPropertyName("first-release-date")]
    public string? FirstReleaseDate { get; init; }

    [JsonPropertyName("isrcs")]
    public IReadOnlyList<string>? Isrcs { get; init; }

    [JsonPropertyName("artist-credit")]
    public IReadOnlyList<ArtistCreditDto>? ArtistCredit { get; init; }

    [JsonPropertyName("aliases")]
    public IReadOnlyList<AliasDto>? Aliases { get; init; }

    [JsonPropertyName("genres")]
    public IReadOnlyList<GenreDto>? Genres { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<GenreDto>? Tags { get; init; }

    [JsonPropertyName("relations")]
    public IReadOnlyList<RelationDto>? Relations { get; init; }

    [JsonPropertyName("releases")]
    public IReadOnlyList<ReleaseDto>? Releases { get; init; }
}
