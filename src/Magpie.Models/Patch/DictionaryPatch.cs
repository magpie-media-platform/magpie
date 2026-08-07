using System.Collections.Generic;

namespace Magpie.Models.Patch;

/// <summary>
/// The patch for a dictionary of values.
/// </summary>
/// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
/// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
/// <typeparam name="TPatch">The type of patch for one existing value.</typeparam>
public sealed record DictionaryPatch<TKey, TValue, TPatch>
{
    /// <summary>
    /// The values to add to the dictionary, keyed by key.
    /// </summary>
    public Dictionary<TKey, TValue>? Add { get; init; }

    /// <summary>
    /// The keys of the values to remove from the dictionary.
    /// </summary>
    public IEnumerable<TKey>? Remove { get; init; }

    /// <summary>
    /// The patch for each existing value, keyed by key.
    /// </summary>
    public Dictionary<TKey, TPatch>? Update { get; init; }
}
