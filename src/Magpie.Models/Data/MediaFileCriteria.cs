using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="MediaFile"/>.
/// </summary>
public sealed record MediaFileCriteria
{
    /// <summary>
    /// The criteria for <see cref="MediaFile.Kind"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> Kind { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MediaFile.MimeType"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> MimeType { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MediaFile.AbsolutePath"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<string>> AbsolutePath { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MediaFile.Size"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<long>> Size { get; init; } = new();
}
