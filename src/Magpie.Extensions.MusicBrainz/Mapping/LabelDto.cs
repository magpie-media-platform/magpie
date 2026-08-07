using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record LabelDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("sort-name")]
    public string? SortName { get; init; }

    [JsonPropertyName("disambiguation")]
    public string? Disambiguation { get; init; }

    [JsonPropertyName("label-code")]
    public int? LabelCode { get; init; }

    [JsonPropertyName("life-span")]
    public LifeSpanDto? LifeSpan { get; init; }

    [JsonPropertyName("isnis")]
    public IReadOnlyList<string>? Isnis { get; init; }

    [JsonPropertyName("ipis")]
    public IReadOnlyList<string>? Ipis { get; init; }

    [JsonPropertyName("relations")]
    public IReadOnlyList<RelationDto>? Relations { get; init; }
}
