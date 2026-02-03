using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="ContentRating"/>.
/// </summary>
public sealed record ContentRatingCriteria
{
    /// <summary>
    /// The criteria for <see cref="ContentRating.RatingsBody"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> RatingsBody { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentRating.Value"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Value { get; init; } = new();
}
