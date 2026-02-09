using System.Collections.Generic;

namespace Magpie.Models.Filtering;

/// <summary>
/// Criteria for collections of criteria
/// </summary>
public record CollectionCriteria<TCriteria>
{
    /// <summary>
    /// Items that must be present (OR within Some, AND between Some and None).
    /// Each item represents a set of conditions that at least one collection item must match.
    /// </summary>
    public List<TCriteria> Some { get; init; } = [];

    /// <summary>
    /// Items that must not be present.
    /// Each item represents a set of conditions that no collection item should match.
    /// </summary>
    public List<TCriteria> None { get; init; } = [];
}
