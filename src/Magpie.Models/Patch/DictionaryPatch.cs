using System.Collections.Generic;

namespace Magpie.Models.Patch;

/// <summary>
/// A patch operation for collections of key-value pairs with add, remove, and update operations.
/// </summary>
/// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
/// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
/// <typeparam name="TPatch">The patch operation for values in the dictionary.</typeparam>
/// <param name="Add"><typeparamref name="TValue"/>s to add to the dictionary.</param>
/// <param name="Remove">Keys of items to remove from the dictionary.</param>
/// <param name="Update">Patches for existing items.</param>
public sealed record DictionaryPatch<TKey, TValue, TPatch>(
    IReadOnlyDictionary<TKey, TValue>? Add,
    IEnumerable<TKey>? Remove,
    IReadOnlyDictionary<TKey, TPatch>? Update);
