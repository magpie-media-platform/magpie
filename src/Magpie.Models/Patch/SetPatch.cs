using System.Collections.Generic;

namespace Magpie.Models.Patch;

/// <summary>
/// The patch for a set of values.
/// </summary>
/// <typeparam name="TItem">The type of items in the collection.</typeparam>
/// <typeparam name="TId">The type of the id of one <typeparamref name="TItem"/>.</typeparam>
public record SetPatch<TItem, TId>
{
    /// <summary>
    /// The items to add to the collection.
    /// </summary>
    public IEnumerable<TItem>? Add { get; init; }

    /// <summary>
    /// The ids of the items to remove from the collection.
    /// </summary>
    public IEnumerable<TId>? Remove { get; init; }
}
