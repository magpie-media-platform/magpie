namespace Magpie.Extensions.Qbittorrent;

internal sealed record TorrentInfo
{
    public string Hash { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string? SavePath { get; set; }

    public string? ContentPath { get; set; }

    public double Progress { get; set; }

    public double Ratio { get; set; }

    public double MaxRatio { get; set; }

    public long SeedingTime { get; set; }

    public long MaxSeedingTime { get; set; }
}
