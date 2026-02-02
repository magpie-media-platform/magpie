using System.Collections.Generic;

namespace Magpie.Models.Filtering;

/// <summary>
/// A list of criteria that all must match.
/// </summary>
/// <typeparam name="TCriteria">The criteria type.</typeparam>
public record AllCriteria<TCriteria>
{
    /// <summary>
    /// The criteria that all must match.
    /// </summary>
    public List<TCriteria> All { get; init; } = [];
}
