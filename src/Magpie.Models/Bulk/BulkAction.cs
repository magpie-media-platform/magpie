using System;
using System.Collections.Generic;

namespace Magpie.Models.Bulk;

/// <summary>
/// The target selection for a bulk operation.
/// </summary>
/// <typeparam name="TFilter">The domain's query-filter type.</typeparam>
public record BulkAction<TFilter> where TFilter : class
{
    /// <summary>
    /// The target ids, or <see langword="null"/> to use only the filter.
    /// </summary>
    public IReadOnlyList<Guid>? Ids { get; init; }

    /// <summary>
    /// The target filter, or <see langword="null"/> to use only the ids.
    /// </summary>
    public TFilter? Filter { get; init; }
}
