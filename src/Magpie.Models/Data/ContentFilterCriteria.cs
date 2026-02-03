using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="ContentItem"/>.
/// </summary>
public sealed record ContentFilterCriteria
{
    /// <summary>
    /// The criteria for related <see cref="ContentHierarchyItem"/>s.
    /// </summary>
    public CriteriaSet<ContentHierarchyItemCriteria> ContentHierarchyItem { get; init; } = new();
    /// <summary>
    /// The criteria for <see cref="ContentItem.MediaFiles"/>.
    /// </summary>
    public CriteriaSet<MediaFileCriteria> MediaFiles { get; init; } = new();
}
