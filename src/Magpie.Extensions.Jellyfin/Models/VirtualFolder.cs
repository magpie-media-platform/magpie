using System.Text.Json.Serialization;

namespace Magpie.Extensions.Jellyfin.Models;

internal sealed record VirtualFolder
{
    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("ItemId")]
    public string? ItemId { get; set; }

    [JsonPropertyName("CollectionType")]
    public string? CollectionType { get; set; }
}
