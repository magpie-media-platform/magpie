using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="AlternateText"/>.
/// </summary>
public sealed record AlternateTextCriteria
{
    /// <summary>
    /// The criteria for <see cref="AlternateText.Kind"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Kind { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="AlternateText.Language"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Language { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="AlternateText.Text"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Text { get; init; } = new();
}
