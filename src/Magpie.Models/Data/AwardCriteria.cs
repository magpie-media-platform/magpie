using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for awards.
/// </summary>
public sealed record AwardCriteria
{
    /// <summary>
    /// Criteria for the body that gives the award.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Body { get; set; } = new();

    /// <summary>
    /// Criteria for the display name of the award.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Name { get; set; } = new();

    /// <summary>
    /// Criteria for the category of the award.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Category { get; set; } = new();

    /// <summary>
    /// Criteria for the status of the award.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Status { get; set; } = new();

    /// <summary>
    /// Criteria for the year of the award.
    /// </summary>
    public CriteriaSet<PropertyCriterion<int>> Year { get; set; } = new();

    /// <summary>
    /// Criteria for the language code of the award.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Language { get; set; } = new();

    /// <summary>
    /// Criteria for the country code of the award.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Country { get; set; } = new();
}
