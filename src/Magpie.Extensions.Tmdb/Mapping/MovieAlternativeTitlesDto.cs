using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record MovieAlternativeTitlesDto
{
    [JsonPropertyName("titles")]
    public IReadOnlyList<AlternativeTitleEntryDto>? Titles { get; init; }
}
