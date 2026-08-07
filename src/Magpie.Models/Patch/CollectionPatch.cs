using System.Collections.Generic;

namespace Magpie.Models.Patch;

/// <summary>
/// The patch for a collection of values.
/// </summary>
/// <typeparam name="TItem">The type of items in the collection.</typeparam>
/// <typeparam name="TId">The type of the id of one <typeparamref name="TItem"/>.</typeparam>
/// <typeparam name="TPatch">The type of patch for one existing item.</typeparam>
public sealed record CollectionPatch<TItem, TId, TPatch> : SetPatch<TItem, TId>
{
    /// <summary>
    /// The patches for existing items.
    /// </summary>
    public IEnumerable<TPatch>? Update { get; init; }
}
