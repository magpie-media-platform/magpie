using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record GuestCrewEntryDto
{
    [JsonPropertyName("person")]
    public PersonDto? Person { get; init; }

    [JsonPropertyName("guestCrewType")]
    public string? Type { get; init; }
}
