using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.OpenLibrary.Mapping;

internal sealed record AuthorDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("personal_name")]
    public string? PersonalName { get; init; }

    [JsonPropertyName("bio")]
    public JsonElement? Bio { get; init; }

    [JsonPropertyName("birth_date")]
    public string? BirthDate { get; init; }

    [JsonPropertyName("death_date")]
    public string? DeathDate { get; init; }

    [JsonPropertyName("photos")]
    public IReadOnlyList<long>? Photos { get; init; }

    [JsonPropertyName("alternate_names")]
    public IReadOnlyList<string>? AlternateNames { get; init; }

    [JsonPropertyName("remote_ids")]
    public AuthorRemoteIdsDto? RemoteIds { get; init; }

    [JsonPropertyName("links")]
    public IReadOnlyList<AuthorLinkDto>? Links { get; init; }

    [JsonPropertyName("wikipedia")]
    public string? Wikipedia { get; init; }
}
