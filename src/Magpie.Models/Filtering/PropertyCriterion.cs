using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Filtering;

/// <summary>
/// One comparison criterion for the value of a property.
/// </summary>
/// <typeparam name="T">The type of the property.</typeparam>
public sealed record PropertyCriterion<T>
{
    /// <summary>
    /// The operator that compares the property to the requested value.
    /// </summary>
    public CriteriaValueOperator Operator { get; init; } = CriteriaValueOperator.Equals;

    /// <summary>
    /// The single value to compare the property to.
    /// </summary>
    public T? Value { get; init; }

    /// <summary>
    /// The set of values to compare the property to, for the set operators.
    /// </summary>
    public IEnumerable<T>? Values { get; init; } = [];

    /// <summary>
    /// Whether the criterion inverts its result.
    /// </summary>
    public bool Negate { get; init; }
}
