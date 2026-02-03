using System.Collections.Generic;

namespace Magpie.Models.Patch;

/// <summary>
/// A patch operation for collections of key-value pairs with add, remove, and update operations.
/// </summary>
/// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
/// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
/// <typeparam name="TPatch">The patch operation for values in the dictionary.</typeparam>
public sealed record DictionaryPatch<TKey, TValue, TPatch>
{
    /// <summary>
    /// <typeparamref name="TValue"/>s to add to the dictionary.
    /// </summary>
    public Dictionary<TKey, TValue>? Add { get; init; }

    /// <summary>
    /// Keys of items to remove from the dictionary.
    /// </summary>
    public IEnumerable<TKey>? Remove { get; init; }

    /// <summary>
    /// Patches for existing items.
    /// </summary>
    public Dictionary<TKey, TPatch>? Update { get; init; }
}
