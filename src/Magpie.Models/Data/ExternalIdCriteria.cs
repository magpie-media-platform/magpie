using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="ExternalId"/>.
/// </summary>
public sealed record ExternalIdCriteria
{
    /// <summary>
    /// The criteria for <see cref="ExternalId.Value"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Value { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ExternalId.Source"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Source { get; init; } = new();
}
