using System.Collections.Generic;

namespace Magpie.Extensions.Jellyfin.Models;

internal sealed record ItemsQueryResult
{
    public List<ItemSummary>? Items { get; set; }
}
