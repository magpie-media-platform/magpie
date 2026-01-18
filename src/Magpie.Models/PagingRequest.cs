namespace Magpie.Models;

/// <summary>
/// Pagination parameters for a query.
/// </summary>
/// <param name="PageNumber">The page number.</param>
/// <param name="PageSize">The number of items per page.</param>
public sealed record PagingRequest(int PageNumber, int PageSize);
