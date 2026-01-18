using System.Collections.Generic;

namespace Magpie.Models.Filters;

/// <summary>
/// A set of criteria where at least one must match.
/// </summary>
/// <typeparam name="T">The criteria type.</typeparam>
public sealed record AnyOfCriteria<T>
{
    /// <summary>
    /// The criteria to match. At least one must match.
    /// </summary>
    public IReadOnlyList<T> Any { get; init; } = [];
}
