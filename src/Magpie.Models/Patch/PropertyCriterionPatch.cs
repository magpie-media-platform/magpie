using System.Collections.Generic;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Models.Patch;

/// <summary>
/// A patch for <see cref="PropertyCriterion{T}"/>.
/// </summary>
public sealed record PropertyCriterionPatch
{
    /// <summary>
    /// The comparison operator patch.
    /// </summary>
    public ScalarPatch<CriteriaValueOperator>? Operator { get; init; }

    /// <summary>
    /// The scalar value to patch.
    /// </summary>
    public ScalarPatch<IEnumerable<string>>? Value { get; init; }

    /// <summary>
    /// The comparison values to patch.
    /// </summary>
    public ArrayPatch<string>? Values { get; init; }
}
