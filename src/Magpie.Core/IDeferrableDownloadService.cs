using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Models.Integrations;
using Magpie.Sdk.Services;

namespace Magpie.Core;

/// <summary>
/// Extends <see cref="IDownloadService"/> to defer the acquisition status and the events to the end of a client
/// refresh.
/// </summary>
internal interface IDeferrableDownloadService : IDownloadService
{
    /// <summary>
    /// Applies refreshed client state, and defers the follow-up work when the status did not change.
    /// </summary>
    /// <param name="download">The tracked download to update</param>
    /// <param name="info">The status as the download client reports it</param>
    /// <param name="userId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>
    /// <see langword="true"/> when the caller must include the download in <see cref="CompleteDeferredRefreshAsync"/>
    /// </returns>
    Task<bool> ApplyDeferredRefreshedClientStateAsync(
        Download download,
        DownloadInfo info,
        Guid? userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates the acquisition status and publishes the events for the deferred downloads.
    /// </summary>
    /// <param name="downloadIds">
    /// The downloads that <see cref="ApplyDeferredRefreshedClientStateAsync"/> deferred
    /// </param>
    /// <param name="userId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>A task that completes after every content item and event is processed</returns>
    Task CompleteDeferredRefreshAsync(
        IReadOnlyCollection<Guid> downloadIds,
        Guid? userId,
        CancellationToken cancellationToken);
}
