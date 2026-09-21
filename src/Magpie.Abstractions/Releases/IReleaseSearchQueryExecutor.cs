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

    /// <summary>
    /// Runs a query plan against the indexers and assesses every release that it finds.
    /// </summary>
    /// <param name="routes">The phases of the query plan to run</param>
    /// <param name="categories">The indexer categories that the content type accepts</param>
    /// <param name="applicableIndexerIds">The ids of the indexers that this search can use</param>
    /// <param name="evaluationContext">The context for the target content hierarchy item</param>
    /// <param name="automatic">True for an automatic search, false for an interactive search</param>
    /// <param name="maximumResults">The largest number of releases to keep from the indexers</param>
    /// <param name="onConnectionStarted">The callback that takes the phase index and the connection name</param>
    /// <param name="useCache">True to answer from the search cache when it holds the same plan</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The assessed releases, the phases that ran, and the connection counts</returns>
    Task<ReleaseQueryExecution> ExecutePlanAsync(
        IReadOnlyList<ReleaseSearchPhase> routes,
        IReadOnlyList<string> categories,
        ISet<Guid> applicableIndexerIds,
        ReleaseEvaluationContext evaluationContext,
        bool automatic,
        int maximumResults,
        Action<int, string>? onConnectionStarted = null,
        bool useCache = true,
        CancellationToken cancellationToken = default);
}
