using System.Text.Json.Serialization;

namespace Magpie.Extensions.Qbittorrent;

internal sealed record TorrentInfo
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("save_path")]
    public string? SavePath { get; set; }

    [JsonPropertyName("content_path")]
    public string? ContentPath { get; set; }

    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    [JsonPropertyName("ratio")]
    public double Ratio { get; set; }

    [JsonPropertyName("max_ratio")]
    public double MaxRatio { get; set; }

    [JsonPropertyName("seeding_time")]
    public long SeedingTime { get; set; }

    [JsonPropertyName("max_seeding_time")]
    public long MaxSeedingTime { get; set; }
}
