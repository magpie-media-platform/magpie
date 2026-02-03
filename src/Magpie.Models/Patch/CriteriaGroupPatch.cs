using Magpie.Models.Filtering;

namespace Magpie.Models.Patch;

/// <summary>
/// Patch for <see cref="CriteriaSet{TCriteria}"/>.
/// </summary>
/// <typeparam name="TCriteria">The criteria type for items in the set.</typeparam>
/// <typeparam name="TCriteriaPatch">The criteria patch type for items in the set.</typeparam>
public sealed record CriteriaSetPatch<TCriteria, TCriteriaPatch>
{
    /// <summary>
    /// The And criteria patch.
    /// </summary>
    public ListPatch<TCriteria, TCriteriaPatch>? And { get; init; }
}

