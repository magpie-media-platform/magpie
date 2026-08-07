using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Jellyfin.Models;

internal sealed record ItemSummary
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("ProviderIds")]
    public Dictionary<string, string>? ProviderIds { get; set; }
}
