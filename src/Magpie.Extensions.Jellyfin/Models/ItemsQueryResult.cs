using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Jellyfin.Models;

internal sealed record ItemsQueryResult
{
    [JsonPropertyName("Items")]
    public List<ItemSummary>? Items { get; set; }
}
