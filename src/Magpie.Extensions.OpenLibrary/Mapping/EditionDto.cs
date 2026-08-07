using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.OpenLibrary.Mapping;

internal sealed record EditionDto
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("number_of_pages")]
    public int? NumberOfPages { get; init; }

    [JsonPropertyName("physical_format")]
    public string? PhysicalFormat { get; init; }

    [JsonPropertyName("series")]
    public IReadOnlyList<string>? Series { get; init; }

    [JsonPropertyName("publish_date")]
    public string? PublishDate { get; init; }

    [JsonPropertyName("covers")]
    public IReadOnlyList<long>? Covers { get; init; }

    [JsonPropertyName("identifiers")]
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Identifiers { get; init; }

    [JsonPropertyName("contributors")]
    public IReadOnlyList<EditionContributorDto>? Contributors { get; init; }

    [JsonPropertyName("contributions")]
    public IReadOnlyList<string>? Contributions { get; init; }
}
