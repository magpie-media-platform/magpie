using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="Image"/>.
/// </summary>
public sealed record ImageCriteria
{
    /// <summary>
    /// The criteria for <see cref="Image.Type"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Type { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Image.MimeType"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> MimeType { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Image.Url"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Url { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Image.Source"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Source { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Image.Language"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Language { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Image.Width"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<int>> Width { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="Image.Height"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<int>> Height { get; init; } = new();
}
