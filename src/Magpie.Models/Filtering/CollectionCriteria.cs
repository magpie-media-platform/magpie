using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Filtering;

/// <summary>
/// Criteria for collections of criteria
/// </summary>
public record CollectionCriteria<TCriteria>
{
    /// <summary>
    /// The operator to apply to the collection
    /// </summary>
    public CollectionCriteriaOperator Operator { get; init; } = CollectionCriteriaOperator.Some;

    /// <summary>
    /// The criteria to apply to items in the collection
    /// </summary>
    public List<TCriteria> Items { get; init; } = [];
}
