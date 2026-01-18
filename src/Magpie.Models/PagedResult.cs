using System.Collections.Generic;

namespace Magpie.Models;

/// <summary>
/// A paginated result set.
/// </summary>
/// <typeparam name="T">The type of items in the result.</typeparam>
/// <param name="Items">The items for the current page.</param>
/// <param name="PageNumber">The current page number (1-based).</param>
/// <param name="PageSize">The number of items per page.</param>
/// <param name="TotalItems">The total number of items across all pages.</param>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int? PageNumber,
    int? PageSize,
    int TotalItems);
