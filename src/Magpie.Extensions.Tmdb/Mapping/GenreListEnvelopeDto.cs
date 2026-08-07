using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record GenreListEnvelopeDto
{
    [JsonPropertyName("genres")]
    public IReadOnlyList<GenreDto>? Genres { get; init; }
}
