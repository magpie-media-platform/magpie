using System.Collections.Generic;

namespace Magpie.Extensions.Jellyfin.Models;

internal sealed record ItemSummary
{
    public string? Id { get; set; }

    public string? Type { get; set; }

    public Dictionary<string, string>? ProviderIds { get; set; }
}
