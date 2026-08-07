using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Wikidata.Mapping;

internal sealed record EntitySearchResponseDto
{
    public IReadOnlyList<EntitySearchHitDto>? Search { get; init; }

    [JsonPropertyName("search-continue")]
    public int? SearchContinue { get; init; }
}
