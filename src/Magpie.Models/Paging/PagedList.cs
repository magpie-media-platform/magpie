using System.Collections.Generic;

namespace Magpie.Models.Paging;

/// <summary>
/// A page of <typeparamref name="T"/> items.
/// </summary>
/// <typeparam name="T">The type of items.</typeparam>
/// <param name="Items">The items for the current page.</param>
/// <param name="PageNumber">The current page number.</param>
/// <param name="PageSize">The number of items per page.</param>
/// <param name="TotalItems">The total number of items.</param>
public sealed record PagedList<T>(IEnumerable<T> Items, int? PageNumber, int? PageSize, int TotalItems);
