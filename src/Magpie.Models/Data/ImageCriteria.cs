using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="Image"/>.
/// </summary>
public sealed record ImageCriteria
{
    /// <summary>
    /// The criteria for <see cref="Image.Kind"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Kind { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Image.MimeType"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> MimeType { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Image.Url"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Url { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Image.Source"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Source { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Image.Language"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Language { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Image.Width"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<int>> Width { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Image.Height"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<int>> Height { get; init; } = new();
}
