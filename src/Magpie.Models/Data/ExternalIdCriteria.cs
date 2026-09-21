using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for external ids.
/// </summary>
public sealed record ExternalIdCriteria
{
    /// <summary>
    /// Criteria for the value of the external id.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Value { get; set; } = new();

    /// <summary>
    /// Criteria for the name of the source.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Source { get; set; } = new();
}
