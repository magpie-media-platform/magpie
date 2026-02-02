using System.Collections.Generic;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Models.Patch;

/// <summary>
/// A patch for <see cref="PropertyCriterion{T}"/>.
/// </summary>
/// <param name="Operator">The comparison operator patch.</param>
/// <param name="Value">The value(s) patch.</param>
public sealed record PropertyCriterionPatch(
    ScalarPatch<CriteriaValueOperator>? Operator,
    ScalarPatch<IEnumerable<string>>? Value = null);
