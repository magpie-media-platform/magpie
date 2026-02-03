using Magpie.Models.Data;
using Magpie.Models.Filtering;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="QualityTier.Criteria"/>. 
/// </summary>
public sealed record QualityTierCriteriaPatch
{
    /// <summary>
    /// The <see cref="CriteriaSet{T}.And"/> patch.
    /// </summary>
    public ListPatch<
        DictionaryCriteria<PropertyCriterion<string>>,
        DictionaryPatch<string, PropertyCriterion<string>,
        PropertyCriterionPatch>>? And
    { get; init; }
}
