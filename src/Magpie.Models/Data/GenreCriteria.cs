using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for genres.
/// </summary>
public sealed record GenreCriteria
{
    /// <summary>
    /// Criteria for the display name of the genre.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Name { get; init; } = new();
}
