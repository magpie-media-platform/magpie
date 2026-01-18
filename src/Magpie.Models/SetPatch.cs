using System.Collections.Generic;

namespace Magpie.Models;

/// <summary>
/// A patch operation for a set-like collection with add and remove only.
/// </summary>
/// <typeparam name="TItem">The type of items in the collection.</typeparam>
/// <typeparam name="TId">The type used to identify items for removal.</typeparam>
/// <param name="Add">Items to add to the collection.</param>
/// <param name="Remove">Identifiers of items to remove from the collection.</param>
public record SetPatch<TItem, TId>(
    IReadOnlyList<TItem>? Add,
    IReadOnlyList<TId>? Remove);
