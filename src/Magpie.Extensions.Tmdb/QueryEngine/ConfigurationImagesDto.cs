namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record ConfigurationImagesDto
{
    [System.Text.Json.Serialization.JsonPropertyName("secure_base_url")]
    public string? SecureBaseUrl { get; init; }
}
