namespace Magpie.Extensions.Jellyfin.Models;

internal sealed record SystemInfo
{
    public string? ServerName { get; set; }

    public string? Version { get; set; }
}
