using Magpie.Models.Enums;

namespace Magpie.Models.Paging;

/// <summary>
/// Sort parameters for a query
/// </summary>
public sealed record SortParameter
{
    /// <summary>
    /// The property name to sort by.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// The direction to sort in.
    /// </summary>
    public required SortDirection Direction { get; init; }
}
