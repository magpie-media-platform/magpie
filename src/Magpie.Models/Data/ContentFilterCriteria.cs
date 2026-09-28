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
    /// The legacy top-level media-file criteria. Magpie rejects a filter that sets them. Use
    /// <see cref="ContentHierarchyItemCriteria.MediaFiles"/>.
    /// </summary>
    public CriteriaSet<MediaFileCriteria> MediaFiles { get; init; } = new();
}
