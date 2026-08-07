using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record CreditsDto
{
    [JsonPropertyName("cast")]
    public IReadOnlyList<CastMemberDto>? Cast { get; init; }

    [JsonPropertyName("crew")]
    public IReadOnlyList<CrewMemberDto>? Crew { get; init; }
}
