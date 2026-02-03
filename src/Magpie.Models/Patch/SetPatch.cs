using System.Collections.Generic;

namespace Magpie.Models.Patch;

/// <summary>
/// A patch for sets of values with add and remove operations.
/// </summary>
/// <typeparam name="TItem">The type of items in the collection.</typeparam>
/// <typeparam name="TId">The unique identifier for <typeparamref name="TItem"/>s.</typeparam>
public record SetPatch<TItem, TId>
{
    /// <summary>
    /// <typeparamref name="TItem"/> items to add to the collection.
    /// </summary>
    public IEnumerable<TItem>? Add { get; init; }

    /// <summary>
    /// Identifiers of items to remove from the collection.
    /// </summary>
    public IEnumerable<TId>? Remove { get; init; }
}
