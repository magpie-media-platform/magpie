using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record CrewMemberDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("job")]
    public string? Job { get; init; }

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; init; }
}
