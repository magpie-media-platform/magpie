using System.Collections.Generic;

namespace Magpie.Models.Filtering;

/// <summary>
/// Criteria for a set of key/value pairs.
/// </summary>
/// <typeparam name="TCriteria">The criteria type for values in the dictionary.</typeparam>
public record DictionaryCriteria<TCriteria>
{
    /// <summary>
    /// The criteria to apply to items in the dictionary.
    /// </summary>
    public Dictionary<string, TCriteria> Items { get; init; } = [];
}
