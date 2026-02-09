using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="Genre"/>.
/// </summary>
public sealed record GenreCriteria
{
    /// <summary>
    /// The criteria for <see cref="Genre.Name"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Name { get; init; } = new();
}
