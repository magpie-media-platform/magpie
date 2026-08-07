using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record MovieReleaseDatesDto
{
    [JsonPropertyName("results")]
    public IReadOnlyList<MovieReleaseDateCountryDto>? Results { get; init; }
}
