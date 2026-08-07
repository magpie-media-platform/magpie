using System.Collections.Generic;

namespace Magpie.Models.Patch;

/// <summary>
/// The patch for a list of values.
/// </summary>
/// <typeparam name="TItem">The type of items in the list.</typeparam>
/// <typeparam name="TPatch">The type of patch for one existing item.</typeparam>
public sealed record ListPatch<TItem, TPatch>
{
    /// <summary>
    /// The patch for each item, keyed by list index.
    /// </summary>
    public Dictionary<int, TPatch>? Update { get; init; }

    /// <summary>
    /// The items to insert, keyed by list index.
    /// </summary>
    public Dictionary<int, TItem>? Insert { get; init; }

    /// <summary>
    /// The indexes of the items to remove from the list.
    /// </summary>
    public IEnumerable<int>? Remove { get; init; }
}
