using System;

using Magpie.Models.Arguments;

namespace Magpie.Models.Api;

/// <summary>
/// The request to update many content items selected by ids, a filter, or both.
/// </summary>
public sealed record BulkContentPatchRequest
{
    /// <summary>
    /// The ids of the content items to update.
    /// </summary>
    public Guid[]? Ids { get; init; }

    /// <summary>
    /// The MagpieQL filter that selects the content items to update.
    /// </summary>
    public string? Filter { get; init; }

    /// <summary>
    /// The patch to apply to every selected content item.
    /// </summary>
    public required ContentHierarchyItemPatch Patch { get; init; }
}
