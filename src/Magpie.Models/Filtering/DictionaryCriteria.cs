using System.Collections.Generic;

namespace Magpie.Models.Filtering;

/// <summary>
/// The criteria for a dictionary of values, keyed by name.
/// </summary>
/// <typeparam name="TCriteria">The type of criterion for one value of the dictionary.</typeparam>
public record DictionaryCriteria<TCriteria>
{
    /// <summary>
    /// The criterion for each dictionary value, keyed by name.
    /// </summary>
    public Dictionary<string, TCriteria> Items { get; init; } = [];
}
