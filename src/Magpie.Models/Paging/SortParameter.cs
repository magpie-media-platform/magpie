using Magpie.Models.Enums;

namespace Magpie.Models.Paging;

/// <summary>
/// Sort instruction for a query.
/// </summary>
public sealed record SortParameter
{
    /// <summary>
    /// Name of the property to sort by.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// Direction of the sort.
    /// </summary>
    public required SortDirection Direction { get; init; }
}
