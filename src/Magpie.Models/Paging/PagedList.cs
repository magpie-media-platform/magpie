using System.Collections.Generic;

namespace Magpie.Models.Paging;

/// <summary>
/// One page of items with its paging data.
/// </summary>
/// <typeparam name="T">The type of the items in the page.</typeparam>
public sealed record PagedList<T>
{
    /// <summary>
    /// The items in the current page.
    /// </summary>
    public IEnumerable<T> Items { get; init; } = [];

    /// <summary>
    /// The current page number.
    /// </summary>
    public int? PageNumber { get; init; }

    /// <summary>
    /// The number of items per page.
    /// </summary>
    public int? PageSize { get; init; }

    /// <summary>
    /// The total number of items across all pages.
    /// </summary>
    public int TotalItems { get; init; }
}
