using System.Collections.Generic;

namespace Magpie.Models.Paging;

/// <summary>
/// A page of <typeparamref name="T"/> items.
/// </summary>
/// <typeparam name="T">The type of items.</typeparam>
public sealed record PagedList<T>
{
    /// <summary>
    /// The items for the current page.
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
    /// The total number of items.
    /// </summary>
    public int TotalItems { get; init; }
}
