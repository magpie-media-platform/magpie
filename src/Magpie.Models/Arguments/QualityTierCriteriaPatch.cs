using Magpie.Models.Filtering;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for quality tier criteria.
/// </summary>
/// <param name="Criteria">The criteria patch.</param>
public sealed record QualityTierCriteriaPatch(
    AllCriteriaPatch<PropertyDictionaryCriteria, PropertyDictionaryCriteriaPatch>? Criteria = null);
