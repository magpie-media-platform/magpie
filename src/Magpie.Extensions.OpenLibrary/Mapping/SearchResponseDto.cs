using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.OpenLibrary.Mapping;

internal sealed record SearchResponseDto
{
    [JsonPropertyName("numFound")]
    public int NumFound { get; init; }

    [JsonPropertyName("numFoundExact")]
    public bool NumFoundExact { get; init; }

    [JsonPropertyName("start")]
    public int Start { get; init; }

    [JsonPropertyName("docs")]
    public IReadOnlyList<SearchDocumentDto>? Docs { get; init; }
}
