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
    public AllCriteria<PropertyCriterion<string>> Value { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ExternalId.Source"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Source { get; init; } = new();
}
