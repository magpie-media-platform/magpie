using System;

namespace Magpie.Models.Api;

/// <summary>
/// The request to delete many content items selected by ids, a filter, or both.
/// </summary>
public sealed record BulkContentDeleteRequest
{
    /// <summary>
    /// The ids of the content items to delete.
    /// </summary>
    public Guid[]? Ids { get; init; }

    /// <summary>
    /// The MagpieQL filter that selects the content items to delete.
    /// </summary>
    public string? Filter { get; init; }

    /// <summary>
    /// Whether the delete also removes the files from disk.
    /// </summary>
    public bool DeleteFromLibrary { get; init; }
}
