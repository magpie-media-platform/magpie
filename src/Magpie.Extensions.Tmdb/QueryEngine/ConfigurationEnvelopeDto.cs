namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record ConfigurationEnvelopeDto
{
    [System.Text.Json.Serialization.JsonPropertyName("images")]
    public ConfigurationImagesDto? Images { get; init; }
}
