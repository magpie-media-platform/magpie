namespace Magpie.Extensions.Jellyfin.Models;

internal sealed record VirtualFolder
{
    public string? Name { get; set; }

    public string? ItemId { get; set; }

    public string? CollectionType { get; set; }
}
