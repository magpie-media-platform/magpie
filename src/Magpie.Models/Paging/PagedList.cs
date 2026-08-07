using System.Collections.Generic;

namespace Magpie.Models.Paging;

/// <summary>
/// A single page of items along with paging metadata.
/// </summary>
/// <typeparam name="T">Type of the items in the page.</typeparam>
public sealed record PagedList<T>
{
    /// <summary>
    /// Items in the current page.
    /// </summary>
    public IEnumerable<T> Items { get; init; } = [];

    /// <summary>
    /// Current page number.
    /// </summary>
    public int? PageNumber { get; init; }

    /// <summary>
    /// Number of items per page.
    /// </summary>
    public int? PageSize { get; init; }

    /// <summary>
    /// Total number of items across all pages.
    /// </summary>
    public int TotalItems { get; init; }
}
