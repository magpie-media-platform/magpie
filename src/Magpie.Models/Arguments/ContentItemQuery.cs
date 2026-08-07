using System;
using System.Collections.Generic;
namespace Magpie.Models.Arguments;

/// <summary>
/// The query criteria for content items.
/// </summary>
public sealed record ContentItemQuery
{
    /// <summary>
    /// The content hierarchy item id to match, or <c>null</c> for all ids.
    /// </summary>
    public Guid? ContentHierarchyItemId { get; init; }

    /// <summary>
    /// The content hierarchy item ids to match, or <c>null</c> for all ids.
    /// </summary>
    public IReadOnlyCollection<Guid>? ContentHierarchyItemIds { get; init; }
}
