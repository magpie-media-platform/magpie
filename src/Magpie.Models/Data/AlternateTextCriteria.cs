using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for alternate text.
/// </summary>
public sealed record AlternateTextCriteria
{
    /// <summary>
    /// Criteria for the kind of alternate text.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Kind { get; init; } = new();

    /// <summary>
    /// Criteria for the language or locale code.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Language { get; init; } = new();

    /// <summary>
    /// Criteria for the alternate text value.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Text { get; init; } = new();
}
