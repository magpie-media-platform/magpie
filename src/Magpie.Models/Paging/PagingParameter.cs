using System;

namespace Magpie.Models.Paging;

/// <summary>
/// Pagination parameters for a query, with a positive page number and page size.
/// </summary>
public sealed record PagingParameter
{
    /// <summary>
    /// One-based page number, clamped to at least one.
    /// </summary>
    public required int PageNumber
    {
        get;
        init => field = Math.Max(1, value);
    }

    /// <summary>
    /// Number of items per page, clamped to the range 1 through 1000.
    /// </summary>
    public required int PageSize
    {
        get;
        init => field = Math.Clamp(value, 1, 1000);
    }
}
