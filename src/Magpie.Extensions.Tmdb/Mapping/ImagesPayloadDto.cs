using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record ImagesPayloadDto
{
    [JsonPropertyName("posters")]
    public IReadOnlyList<ImageEntryDto>? Posters { get; init; }

    [JsonPropertyName("backdrops")]
    public IReadOnlyList<ImageEntryDto>? Backdrops { get; init; }

    [JsonPropertyName("logos")]
    public IReadOnlyList<ImageEntryDto>? Logos { get; init; }

    [JsonPropertyName("stills")]
    public IReadOnlyList<ImageEntryDto>? Stills { get; init; }

    [JsonPropertyName("profiles")]
    public IReadOnlyList<ImageEntryDto>? Profiles { get; init; }
}
