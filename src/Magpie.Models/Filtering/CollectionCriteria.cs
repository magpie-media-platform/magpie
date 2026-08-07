using System.Collections.Generic;
using System.Linq;

namespace Magpie.Models.Filtering;

/// <summary>
/// The criteria for a collection of values.
/// </summary>
/// <typeparam name="TCriteria">The type of criterion for one item of the collection.</typeparam>
public record CollectionCriteria<TCriteria>
{
    /// <summary>
    /// The criteria groups that must all match. One match in a group is sufficient for that group.
    /// </summary>
    public List<List<TCriteria>> Some { get; init; } = [];

    /// <summary>
    /// The criteria that must not match.
    /// </summary>
    public List<TCriteria> None { get; init; } = [];

    /// <summary>
    /// All criteria of <see cref="Some"/> as one sequence.
    /// </summary>
    public IEnumerable<TCriteria> SomeFlat => Some.SelectMany(group => group);
}
