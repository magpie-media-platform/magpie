using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Releases;
using Magpie.Sdk.Jobs;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for releases and their acquisition.
/// </summary>
public interface IReleaseService
{
    /// <summary>
    /// Gets the unified release view of a content hierarchy item, or <see langword="null"/> when it is absent.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item to read</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<ContentRelease>?> GetReleaseViewAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a content release.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="releaseId">The id of the release</param>
    /// <param name="patch">The patch to apply</param>
    /// <param name="userId">The user to record as the actor</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>Whether the content release exists</returns>
    Task<bool> PatchContentReleaseAsync(
        Guid contentHierarchyItemId,
        Guid releaseId,
        ContentReleasePatch patch,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires a release with an eligible download client.
    /// </summary>
    /// <param name="releaseId">The id of the release to acquire</param>
    /// <param name="userId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="progress">The reporter for progress updates, or <see langword="null"/> when there is none</param>
    /// <param name="interactive">
    /// Whether the grab is interactive, which prior Failed or Imported downloads do not suppress; the backoff after a
    /// Failed or Faulted attempt applies to it too
    /// </param>
    /// <param name="grabTargetContentHierarchyItemId">The hierarchy node that the grab targets</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Download?> AcquireAsync(
        Guid releaseId,
        Guid? userId,
        Guid? correlationId = null,
        IProgressReporter? progress = null,
        bool interactive = false,
        Guid? grabTargetContentHierarchyItemId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects the best stored release for a content item, or <see langword="null"/> when none is grabbable.
    /// </summary>
    /// <param name="contentItemId">The id of the content item to select a release for</param>
    /// <param name="userId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Release?> SelectBestReleaseAsync(
        Guid contentItemId,
        Guid? userId = null,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects every acceptable stored release for a content item, best first, except releases that already have an
    /// active download.
    /// </summary>
    /// <param name="contentItemId">The id of the content item to select releases for</param>
    /// <param name="userId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<Release>> SelectAcceptableReleasesAsync(
        Guid contentItemId,
        Guid? userId = null,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default);
}
