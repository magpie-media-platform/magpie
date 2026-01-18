using System.Collections.Generic;

namespace Magpie.Models.Filters;

/// <summary>
/// A criterion for a field.
/// </summary>
/// <typeparam name="T">The type of value being filtered.</typeparam>
/// <param name="Operator">The comparison operator.</param>
/// <param name="Value">The value for scalar operators.</param>
/// <param name="Range">The range for range operators.</param>
/// <param name="Values">The set of values for collection operators.</param>
public sealed record FieldCriterion<T>(
    FieldOperator Operator,
    T? Value = default,
    Range<T>? Range = null,
    IReadOnlyList<T>? Values = null);
