using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record SearchEnvelopeDto<THit>
{
    [JsonPropertyName("page")]
    public int Page { get; init; }

    [JsonPropertyName("results")]
    public IReadOnlyList<THit>? Results { get; init; }

    [JsonPropertyName("total_results")]
    public int TotalResults { get; init; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; init; }
}
