using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record ReleaseDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("date")]
    public string? Date { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("text-representation")]
    public TextRepresentationDto? TextRepresentation { get; init; }

    [JsonPropertyName("release-group")]
    public ReleaseGroupDto? ReleaseGroup { get; init; }

    [JsonPropertyName("artist-credit")]
    public IReadOnlyList<ArtistCreditDto>? ArtistCredit { get; init; }

    [JsonPropertyName("media")]
    public IReadOnlyList<MediumDto>? Media { get; init; }

    [JsonPropertyName("label-info")]
    public IReadOnlyList<LabelInfoDto>? LabelInfo { get; init; }

    [JsonPropertyName("relations")]
    public IReadOnlyList<RelationDto>? Relations { get; init; }
}
