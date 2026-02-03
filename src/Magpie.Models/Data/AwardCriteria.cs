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
    public CriteriaSet<PropertyCriterion<string>> AwardBody { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Award.Name"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Name { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Award.Category"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Category { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Award.Status"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Status { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Award.Year"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<int>> Year { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Award.Language"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Language { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Award.Country"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Country { get; init; } = new();
}
