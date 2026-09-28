using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Releases;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for interactive and automatic indexer searches.
/// </summary>
public interface IReleaseSearchService
{
    /// <summary>
    /// Grabs a release that a user chose for a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item the release fulfils</param>
    /// <param name="release">The release to grab</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The grab outcome status, the queued acquisition execution id, and a problem detail</returns>
    Task<InteractiveReleaseGrabOutcome> GrabInteractiveAsync(
        Guid contentHierarchyItemId,
        Release release,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs an interactive indexer search for a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item to search for</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The candidates, or <see langword="null"/> when the item is absent</returns>
    Task<IReadOnlyList<InteractiveSearchCandidate>?> SearchInteractiveAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs an interactive search and reports incomplete provider branches.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item to search for</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The search result, or null when the item is absent</returns>
    async Task<InteractiveSearchResult?> SearchInteractiveResultAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        var candidates = await SearchInteractiveAsync(contentHierarchyItemId, cancellationToken).ConfigureAwait(false);
        return candidates is null ? null : new InteractiveSearchResult { Candidates = candidates };
    }

    /// <summary>
    /// Searches the indexers for a content hierarchy item and stores the resulting releases.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item to search for</param>
    /// <param name="userId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="currentExecutionId">The job execution that runs the search, or <see langword="null"/></param>
    /// <param name="progress">The reporter for progress updates, or <see langword="null"/> when there is none</param>
    /// <param name="force">Whether the search bypasses normal suppression and eligibility checks</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task SearchAsync(
        Guid contentHierarchyItemId,
        Guid? userId,
        Guid? correlationId,
        Guid? currentExecutionId,
        IProgressReporter? progress,
        bool force = false,
        CancellationToken cancellationToken = default);
}
