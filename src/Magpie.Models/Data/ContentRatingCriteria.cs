using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="ContentRating"/>.
/// </summary>
public sealed record ContentRatingCriteria
{
    /// <summary>
    /// The criteria for <see cref="ContentRating.Body"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Body { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentRating.Rating"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Rating { get; init; } = new();
}
