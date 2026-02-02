using System.Collections.Generic;

namespace Magpie.Models.Patch;

/// <summary>
/// A patch for a list of values with add, remove, and indexed update operations.
/// </summary>
/// <typeparam name="TItem">The type of items in the list.</typeparam>
/// <typeparam name="TPatch">The type of patch operation for existing items.</typeparam>
/// <param name="Insert">Indexed <typeparamref name="TItem"/>s to insert into the list.</param>
/// <param name="Remove">Indexes of items to remove from the list.</param>
/// <param name="Update"><typeparamref name="TPatch"/> patches at specific indexes of the list.</param>
public sealed record ListPatch<TItem, TPatch>(
    IReadOnlyDictionary<int, TItem>? Insert = null,
    IEnumerable<int>? Remove = null,
    IReadOnlyDictionary<int, TPatch>? Update = null) : ArrayPatch<TPatch>(Update);
