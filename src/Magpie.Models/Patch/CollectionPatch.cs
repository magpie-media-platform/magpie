using System.Collections.Generic;

namespace Magpie.Models.Patch;

/// <summary>
/// A patch for collections of values with add, remove, and update operations.
/// </summary>
/// <typeparam name="TItem">The type of items in the collection.</typeparam>
/// <typeparam name="TId">The unique identifier for <typeparamref name="TItem"/>s.</typeparam>
/// <typeparam name="TPatch">The type of patch operation for existing items.</typeparam>
/// <param name="Add"><typeparamref name="TItem"/> items to add to the collection.</param>
/// <param name="Remove">Identifiers of items to remove from the collection.</param>
/// <param name="Update">Patches for existing items.</param>
public sealed record CollectionPatch<TItem, TId, TPatch>(
    IEnumerable<TItem>? Add = null,
    IEnumerable<TId>? Remove = null,
    IEnumerable<TPatch>? Update = null) : SetPatch<TItem, TId>(Add, Remove);
