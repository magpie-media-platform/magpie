using System.Collections.Generic;

namespace Magpie.Models.Filtering;

/// <summary>
/// Criteria for a set of key/value pairs.
/// </summary>
/// <typeparam name="TCriteria">The criteria type for values in the dictionary.</typeparam>
public record DictionaryCriteria<TCriteria>
{
    /// <summary>
    /// The criteria to match entries against, keyed by dictionary key.
    /// </summary>
    public Dictionary<string, TCriteria> Criteria { get; init; } = [];
}
