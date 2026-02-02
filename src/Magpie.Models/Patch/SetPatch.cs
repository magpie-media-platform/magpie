using System.Collections.Generic;

namespace Magpie.Models.Patch;

/// <summary>
/// A patch for sets of values with add and remove operations.
/// </summary>
/// <typeparam name="TItem">The type of items in the collection.</typeparam>
/// <typeparam name="TId">The unique identifier for <typeparamref name="TItem"/>s.</typeparam>
/// <param name="Add"><typeparamref name="TItem"/> items to add to the collection.</param>
/// <param name="Remove">Identifiers of items to remove from the collection.</param>
public record SetPatch<TItem, TId>(IEnumerable<TItem>? Add = null, IEnumerable<TId>? Remove = null);
