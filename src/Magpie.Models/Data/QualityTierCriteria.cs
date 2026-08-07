using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// The match criteria of a quality tier.
/// </summary>
public record QualityTierCriteria : CriteriaSet<DictionaryCriteria<PropertyCriterion<string>>>
{
}
