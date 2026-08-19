using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Releases;

namespace Magpie.Abstractions.Releases;

/// <summary>
/// The executor contract for a staged release-search query plan.
/// </summary>
public interface IReleaseSearchQueryExecutor
{
    /// <summary>
    /// Runs the phases of a query plan and collects the releases that they find.
    /// </summary>
    /// <param name="phases">The phases of the query plan to run</param>
    /// <param name="categories">The indexer categories that the content type accepts</param>
    /// <param name="applicableIndexerIds">The ids of the indexers that this search can use</param>
    /// <param name="onConnectionStarted">The callback that takes the phase index and the connection name</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <param name="selectedConnectionIds">
    /// The ids of the only indexer connections to query, or <see langword="null"/> for every enabled connection
    /// </param>
    /// <returns>The phases with their releases, candidates, and connection outcomes</returns>
    Task<IReadOnlyList<ReleaseSearchPhase>> ExecuteAsync(
        IReadOnlyList<ReleaseSearchPhase> phases,
        IReadOnlyList<string> categories,
        ISet<Guid> applicableIndexerIds,
        Action<int, string>? onConnectionStarted = null,
        CancellationToken cancellationToken = default,
        ISet<Guid>? selectedConnectionIds = null);
}
