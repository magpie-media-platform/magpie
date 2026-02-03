using System.Collections.Generic;

namespace Magpie.Models.Filtering;

/// <summary>
/// A set of criteria where all constraints must be true.
/// </summary>
/// <typeparam name="TCriteria">The criteria type.</typeparam>
public record CriteriaSet<TCriteria>
{
    /// <summary>
    /// The criteria constraints that must all match. 
    /// </summary>
    public IEnumerable<TCriteria> And { get; init; } = [];
}
