using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record MovieReleaseDateCountryDto
{
    [JsonPropertyName("iso_3166_1")]
    public string? CountryCode { get; init; }

    [JsonPropertyName("release_dates")]
    public IReadOnlyList<ReleaseDateEntryDto>? ReleaseDates { get; init; }
}
