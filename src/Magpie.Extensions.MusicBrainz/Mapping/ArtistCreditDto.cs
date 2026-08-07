using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record ArtistCreditDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("artist")]
    public ArtistDto? Artist { get; init; }

    [JsonPropertyName("joinphrase")]
    public string? JoinPhrase { get; init; }
}
