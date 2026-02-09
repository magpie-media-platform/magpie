using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="MediaFile"/>.
/// </summary>
public sealed record MediaFileCriteria
{
    /// <summary>
    /// The criteria for <see cref="MediaFile.FileKind"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> FileKind { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MediaFile.MimeType"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> MimeType { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MediaFile.FilePath"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> FilePath { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="MediaFile.FileSize"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<long>> FileSize { get; init; } = new();
}
