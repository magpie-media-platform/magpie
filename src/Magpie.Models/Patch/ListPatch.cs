using System.Collections.Generic;

namespace Magpie.Models.Patch;

/// <summary>
/// A patch for a list of values with add, remove, and indexed update operations.
/// </summary>
/// <typeparam name="TItem">The type of items in the list.</typeparam>
/// <typeparam name="TPatch">The type of patch operation for existing items.</typeparam>
public sealed record ListPatch<TItem, TPatch> : ArrayPatch<TPatch>
{
    /// <summary>
    /// Indexes of items to remove from the list.
    /// </summary>
    public Dictionary<int, TItem>? Insert { get; init; }

    /// <summary>
    /// Indexes of items to remove from the list.
    /// </summary>
    public IEnumerable<int>? Remove { get; init; }
}
