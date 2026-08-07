using Magpie.Models.Enums;
using Magpie.Models.Paging;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for discovered wishlist items.
/// </summary>
public sealed record WishlistDiscoveredQuery
{
    /// <summary>
    /// The page number and page size.
    /// </summary>
    public required PagingParameter Paging { get; init; }

    /// <summary>
    /// The library presence to match.
    /// </summary>
    public bool? InLibrary { get; init; }

    /// <summary>
    /// The discovery status to match.
    /// </summary>
    public WishlistDiscoveredItemStatus? Status { get; init; }

    /// <summary>
    /// The field to sort the results by.
    /// </summary>
    public WishlistDiscoveredSort Sort { get; init; } = WishlistDiscoveredSort.Discovered;

    /// <summary>
    /// Whether to sort in descending order.
    /// </summary>
    public bool? SortDescending { get; init; }

    /// <summary>
    /// The title substring to match.
    /// </summary>
    public string? TitleSearch { get; init; }
}
