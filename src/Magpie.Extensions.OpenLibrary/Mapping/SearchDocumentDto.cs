using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.OpenLibrary.Mapping;

internal sealed record SearchDocumentDto
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonPropertyName("alternative_title")]
    public IReadOnlyList<string>? AlternativeTitles { get; init; }

    [JsonPropertyName("author_name")]
    public IReadOnlyList<string>? AuthorNames { get; init; }

    [JsonPropertyName("author_key")]
    public IReadOnlyList<string>? AuthorKeys { get; init; }

    [JsonPropertyName("first_publish_year")]
    public int? FirstPublishYear { get; init; }

    [JsonPropertyName("language")]
    public IReadOnlyList<string>? Languages { get; init; }

    [JsonPropertyName("subject")]
    public IReadOnlyList<string>? Subjects { get; init; }

    [JsonPropertyName("subject_key")]
    public IReadOnlyList<string>? SubjectKeys { get; init; }

    [JsonPropertyName("cover_i")]
    public long? CoverId { get; init; }

    [JsonPropertyName("cover_edition_key")]
    public string? CoverEditionKey { get; init; }

    [JsonPropertyName("isbn")]
    public IReadOnlyList<string>? Isbns { get; init; }

    [JsonPropertyName("number_of_pages_median")]
    public int? NumberOfPagesMedian { get; init; }

    [JsonPropertyName("publisher")]
    public IReadOnlyList<string>? Publishers { get; init; }

    [JsonPropertyName("description")]
    public JsonElement? Description { get; init; }

    [JsonPropertyName("person")]
    public IReadOnlyList<string>? Persons { get; init; }

    [JsonPropertyName("person_key")]
    public IReadOnlyList<string>? PersonKeys { get; init; }

    [JsonPropertyName("place")]
    public IReadOnlyList<string>? Places { get; init; }

    [JsonPropertyName("place_key")]
    public IReadOnlyList<string>? PlaceKeys { get; init; }

    [JsonPropertyName("time")]
    public IReadOnlyList<string>? Times { get; init; }

    [JsonPropertyName("time_key")]
    public IReadOnlyList<string>? TimeKeys { get; init; }

    [JsonPropertyName("publish_date")]
    public IReadOnlyList<string>? PublishDates { get; init; }

    [JsonPropertyName("oclc")]
    public IReadOnlyList<string>? Oclcs { get; init; }

    [JsonPropertyName("lccn")]
    public IReadOnlyList<string>? Lccns { get; init; }

    [JsonPropertyName("id_goodreads")]
    public IReadOnlyList<string>? GoodreadsIds { get; init; }
}
