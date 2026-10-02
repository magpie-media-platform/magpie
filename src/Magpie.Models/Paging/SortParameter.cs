using Magpie.Models.Enums;

namespace Magpie.Models.Paging;

/// <summary>
/// The sort instruction for a query.
/// </summary>
public sealed record SortParameter
{
    /// <summary>
    /// The name of the property to sort by.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// The direction of the sort.
    /// </summary>
    public required SortDirection Direction { get; init; }
}
