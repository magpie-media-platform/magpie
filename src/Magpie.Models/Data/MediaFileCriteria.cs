using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// The filter criteria for a <see cref="MediaFile"/>.
/// </summary>
public sealed record MediaFileCriteria
{
    /// <summary>
    /// The criteria on the file role, from <see cref="Enums.FileKind"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> FileKind { get; init; } = new();

    /// <summary>
    /// The criteria on the MIME type of the file contents.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> MimeType { get; init; } = new();

    /// <summary>
    /// The criteria on the full path of the file on disk.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> FilePath { get; init; } = new();

    /// <summary>
    /// The criteria on the size of the file in bytes.
    /// </summary>
    public CriteriaSet<PropertyCriterion<long>> FileSize { get; init; } = new();
}
