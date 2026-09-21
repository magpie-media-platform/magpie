using System;

namespace Magpie.Models.Data;

/// <summary>
/// The place of one content hierarchy item in the tree.
/// </summary>
public sealed record ContentHierarchyItemAncestry
{
    /// <summary>
    /// The id of the content hierarchy item.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the parent item, or <see langword="null"/> for a root item.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// The effective monitoring value of the item.
    /// </summary>
    public bool EffectiveMonitoring { get; set; }
}
