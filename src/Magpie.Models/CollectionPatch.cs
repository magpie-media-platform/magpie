using System.Collections.Generic;

namespace Magpie.Models;

/// <summary>
/// A patch operation for a collection field with add, remove, and update.
/// </summary>
/// <typeparam name="TItem">The type of items in the collection.</typeparam>
/// <typeparam name="TId">The type used to identify items for removal.</typeparam>
/// <typeparam name="TPatch">The patch type for updating items in the collection.</typeparam>
/// <param name="Add">Items to add to the collection.</param>
/// <param name="Remove">Identifiers of items to remove from the collection.</param>
/// <param name="Update">Patches for existing items.</param>
public sealed record CollectionPatch<TItem, TId, TPatch>(
    IReadOnlyList<TItem>? Add,
    IReadOnlyList<TId>? Remove,
    IReadOnlyList<TPatch>? Update) : SetPatch<TItem, TId>(Add, Remove);
