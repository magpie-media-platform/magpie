using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    /// Acquires a release with an eligible download client.
    /// </summary>
    /// <param name="releaseId">The id of the release to acquire</param>
    /// <param name="userId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="progress">The reporter for progress updates, or <see langword="null"/> when there is none</param>
    /// <param name="suspectLastResort">Whether the release is a flagged last-resort suspect grab</param>
    /// <param name="ignoreFailedTaint">Whether the grab proceeds despite prior failed downloads</param>
    /// <param name="grabTargetContentHierarchyItemId">The hierarchy node that the grab targets</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Download?> AcquireAsync(
        Guid releaseId,
        Guid? userId,
        Guid? correlationId = null,
        IProgressReporter? progress = null,
        bool suspectLastResort = false,
        bool ignoreFailedTaint = false,
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
}
