using System.Collections.Generic;

namespace Magpie.Models.Filters;

/// <summary>
/// Criteria for a collection field.
/// </summary>
/// <typeparam name="TCriteria">The criteria type for items in the collection.</typeparam>
public sealed record CollectionCriteria<TCriteria>
{
    /// <summary>
    /// The criteria to match items against.
    /// </summary>
    public IReadOnlyList<TCriteria> Criteria { get; init; } = [];
    /// <summary>
    /// How to match against the collection.
    /// </summary>
    public CollectionOperator Operator { get; init; } = CollectionOperator.Any;
}
