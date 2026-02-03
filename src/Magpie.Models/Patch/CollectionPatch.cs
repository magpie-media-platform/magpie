using System.Collections.Generic;

namespace Magpie.Models.Patch;

/// <summary>
/// A patch for collections of values with add, remove, and update operations.
/// </summary>
/// <typeparam name="TItem">The type of items in the collection.</typeparam>
/// <typeparam name="TId">The unique identifier for <typeparamref name="TItem"/>s.</typeparam>
/// <typeparam name="TPatch">The type of patch operation for existing items.</typeparam>
public sealed record CollectionPatch<TItem, TId, TPatch> : SetPatch<TItem, TId>
{
    /// <summary>
    /// Patches for existing items.
    /// </summary>
    public IEnumerable<TPatch>? Update { get; init; }
}


