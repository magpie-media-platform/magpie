using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record ArtistDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("sort-name")]
    public string? SortName { get; init; }

    [JsonPropertyName("disambiguation")]
    public string? Disambiguation { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("life-span")]
    public LifeSpanDto? LifeSpan { get; init; }

    [JsonPropertyName("release-groups")]
    public IReadOnlyList<ReleaseGroupDto>? ReleaseGroups { get; init; }

    [JsonPropertyName("aliases")]
    public IReadOnlyList<AliasDto>? Aliases { get; init; }

    [JsonPropertyName("genres")]
    public IReadOnlyList<GenreDto>? Genres { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<GenreDto>? Tags { get; init; }

    [JsonPropertyName("relations")]
    public IReadOnlyList<RelationDto>? Relations { get; init; }

    [JsonPropertyName("isnis")]
    public IReadOnlyList<string>? Isnis { get; init; }

    [JsonPropertyName("ipis")]
    public IReadOnlyList<string>? Ipis { get; init; }
}
