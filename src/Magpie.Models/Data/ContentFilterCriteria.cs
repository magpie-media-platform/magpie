using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for a content filter.
/// </summary>
public sealed record ContentFilterCriteria
{
    /// <summary>
    /// Criteria for the <see cref="ContentHierarchyItem"/>.
    /// </summary>
    public CriteriaSet<ContentHierarchyItemCriteria> ContentHierarchyItem { get; init; } = new();

    /// <summary>
    /// Criteria for the media files of the content.
    /// </summary>
    public CriteriaSet<MediaFileCriteria> MediaFiles { get; init; } = new();
}
