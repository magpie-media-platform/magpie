using System.Collections.Generic;

namespace Magpie.Models.Filters;

/// <summary>
/// Criteria for a dictionary field.
/// </summary>
/// <typeparam name="TCriteria">The criteria type for values in the dictionary.</typeparam>
public sealed record DictionaryCriteria<TCriteria>
{
    /// <summary>
    /// The criteria to match entries against, keyed by dictionary key.
    /// </summary>
    public IReadOnlyDictionary<string, TCriteria> Criteria { get; init; } = new Dictionary<string, TCriteria>();
    /// <summary>
    /// How to match against the dictionary.
    /// </summary>
    public CollectionOperator Operator { get; init; } = CollectionOperator.All;
}
