using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// Duplicate root items.
/// </summary>
public sealed record DuplicateRoots
{
    /// <summary>
    /// The source of the external identity that the duplicates share.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// The value of the external identity that the duplicates share.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// The names of the duplicate root items, by item id.
    /// </summary>
    public required IReadOnlyDictionary<Guid, string> Items { get; init; }
}
