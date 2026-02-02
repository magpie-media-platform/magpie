using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="Award"/>.
/// </summary>
public sealed record AwardCriteria
{
    /// <summary>
    /// The criteria for <see cref="Award.AwardBody"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> AwardBody { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Award.Name"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Name { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Award.Category"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Category { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Award.Status"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Status { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Award.Year"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<int>> Year { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Award.Language"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Language { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Award.Country"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Country { get; init; } = new();
}
