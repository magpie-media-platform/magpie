using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.Mapping;

internal sealed record PersonDetailDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("biography")]
    public string? Biography { get; init; }

    [JsonPropertyName("birthday")]
    public string? Birthday { get; init; }

    [JsonPropertyName("deathday")]
    public string? Deathday { get; init; }

    [JsonPropertyName("place_of_birth")]
    public string? PlaceOfBirth { get; init; }

    [JsonPropertyName("gender")]
    public int Gender { get; init; }

    [JsonPropertyName("also_known_as")]
    public IReadOnlyList<string>? AlsoKnownAs { get; init; }

    [JsonPropertyName("known_for_department")]
    public string? KnownForDepartment { get; init; }

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; init; }

    [JsonPropertyName("external_ids")]
    public ExternalIdsDto? ExternalIds { get; init; }

    [JsonPropertyName("images")]
    public ImagesPayloadDto? Images { get; init; }
}
