using System;

namespace Magpie.Models.Paging;

/// <summary>
/// The paging parameters for a query.
/// </summary>
public sealed record PagingParameter
{
    /// <summary>
    /// The one-based page number.
    /// </summary>
    public required int PageNumber
    {
        get;
        init => field = Math.Max(1, value);
    }

    /// <summary>
    /// The number of items per page.
    /// </summary>
    public required int PageSize
    {
        get;
        init => field = Math.Clamp(value, 1, 1000);
    }
}
