using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record ShowEmbeddedDto
{
    [JsonPropertyName("episodes")]
    public IReadOnlyList<EpisodeDto>? Episodes { get; init; }

    [JsonPropertyName("images")]
    public IReadOnlyList<ImageDto>? Images { get; init; }

    [JsonPropertyName("akas")]
    public IReadOnlyList<AkaDto>? Akas { get; init; }

    [JsonPropertyName("seasons")]
    public IReadOnlyList<SeasonDto>? Seasons { get; init; }

    [JsonPropertyName("cast")]
    public IReadOnlyList<CastEntryDto>? Cast { get; init; }

    [JsonPropertyName("crew")]
    public IReadOnlyList<CrewEntryDto>? Crew { get; init; }
}
