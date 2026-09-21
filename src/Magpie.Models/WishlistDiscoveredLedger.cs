using Magpie.Models.Data;
using Magpie.Models.Paging;

namespace Magpie.Models;

/// <summary>
/// A page of discovered wishlist rows with the aggregate library-presence counts.
/// </summary>
public sealed record WishlistDiscoveredLedger
{
    /// <summary>
    /// The requested page of discovered rows.
    /// </summary>
    public required PagedList<WishlistDiscoveredEnrichedRow> Page { get; init; }

    /// <summary>
    /// The aggregate counts across all discovered rows.
    /// </summary>
    public required WishlistDiscoveredCounts Counts { get; init; }
}
