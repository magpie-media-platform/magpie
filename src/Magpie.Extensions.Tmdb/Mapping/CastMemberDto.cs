using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record CastMemberDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("character")]
    public string? Character { get; init; }

    [JsonPropertyName("order")]
    public int? Order { get; init; }

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; init; }
}
