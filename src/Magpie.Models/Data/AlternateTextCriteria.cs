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
    public AllCriteria<PropertyCriterion<string>> Kind { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="AlternateText.Language"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Language { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="AlternateText.Text"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Text { get; init; } = new();
}
