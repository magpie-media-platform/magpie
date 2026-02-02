using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Filtering;

/// <summary>
/// A criterion for a property.
/// </summary>
/// <typeparam name="T">The type of the property.</typeparam>
public sealed record PropertyCriterion<T>
{
    /// <summary>
    /// The comparison operator.
    /// </summary>
    public CriteriaValueOperator Operator { get; init; } = CriteriaValueOperator.Equals;

    /// <summary>
    /// The scalar value to compare against.
    /// </summary>
    public T? Value { get; init; }

    /// <summary>
    /// The values to compare against.
    /// </summary>
    public List<T>? Values { get; init; } = [];
}
