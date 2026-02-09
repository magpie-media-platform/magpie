using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="Tag"/>.
/// </summary>
public sealed record TagCriteria
{
    /// <summary>
    /// The criteria for <see cref="Tag.Name"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Name { get; init; } = new();
}
