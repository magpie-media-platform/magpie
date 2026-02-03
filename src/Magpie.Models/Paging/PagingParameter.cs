namespace Magpie.Models.Paging;

/// <summary>
/// Pagination parameters for a query.
/// </summary>
public sealed record PagingParameter
{
    /// <summary>
    /// The page number.
    /// </summary>
    public required int PageNumber { get; init; }

    /// <summary>
    /// The number of items per page.
    /// </summary>
    public required int PageSize { get; init; }
}
