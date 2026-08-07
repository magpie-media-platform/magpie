using System.Collections.Generic;

namespace Magpie.Models.Filtering;

/// <summary>
/// A group of criteria that must all be true.
/// </summary>
/// <typeparam name="TCriteria">The type of criterion in the group.</typeparam>
public record CriteriaSet<TCriteria>
{
    /// <summary>
    /// The criteria that must all be true.
    /// </summary>
    public IEnumerable<TCriteria> And { get; init; } = [];
}
