using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// The filter criteria for a <see cref="Tag"/>.
/// </summary>
public sealed record TagCriteria
{
    /// <summary>
    /// The criteria on the name of the tag.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Name { get; init; } = new();
}
