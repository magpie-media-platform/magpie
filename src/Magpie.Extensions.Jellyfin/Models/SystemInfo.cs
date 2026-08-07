using System.Text.Json.Serialization;

namespace Magpie.Extensions.Jellyfin.Models;

internal sealed record SystemInfo
{
    [JsonPropertyName("ServerName")]
    public string? ServerName { get; set; }

    [JsonPropertyName("Version")]
    public string? Version { get; set; }
}
