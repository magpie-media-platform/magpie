using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record TrackDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("position")]
    public int? Position { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("length")]
    public long? Length { get; init; }

    [JsonPropertyName("artist-credit")]
    public IReadOnlyList<ArtistCreditDto>? ArtistCredit { get; init; }

    [JsonPropertyName("recording")]
    public RecordingDto? Recording { get; init; }
}
