using Magpie.Models.Filtering;

namespace Magpie.Models.Patch;

/// <summary>
/// Patch for <see cref="AllCriteria{TCriteria}"/>.
/// </summary>
/// <typeparam name="TCriteria">The criteria type for items in the set.</typeparam>
/// <typeparam name="TCriteriaPatch">The criteria patch type for items in the set.</typeparam>
/// <param name="Criteria">The criteria patch</param>
public sealed record AllCriteriaPatch<TCriteria, TCriteriaPatch>(ListPatch<TCriteria, TCriteriaPatch>? Criteria = null);
