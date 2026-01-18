using System.Collections.Generic;

namespace Magpie.Models;

/// <summary>
/// A patch operation for a dictionary-like collection with add, remove, and update.
/// </summary>
/// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
/// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
/// <typeparam name="TPatch">The patch type for updating values in the dictionary.</typeparam>
/// <param name="Add">Items to add to the dictionary.</param>
/// <param name="Remove">Keys of items to remove from the dictionary.</param>
/// <param name="Update">Patches for existing items.</param>
public sealed record DictionaryPatch<TKey, TValue, TPatch>(
    IReadOnlyDictionary<TKey, TValue>? Add,
    IReadOnlyList<TKey>? Remove,
    IReadOnlyDictionary<TKey, TPatch>? Update);
