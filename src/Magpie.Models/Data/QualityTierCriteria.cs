using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// The criteria for a quality tier.
/// </summary>
public record QualityTierCriteria : AllCriteria<DictionaryCriteria<PropertyCriterion<string>>>
{
}
