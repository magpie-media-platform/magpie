using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for content ratings.
/// </summary>
public sealed record ContentRatingCriteria
{
    /// <summary>
    /// Criteria for the body that gives the rating.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Body { get; init; } = new();

    /// <summary>
    /// Criteria for the rating value.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Rating { get; init; } = new();
}
