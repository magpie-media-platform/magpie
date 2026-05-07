using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Abstractions.Integrations;

/// <summary>
/// Integration with an external media server.
/// </summary>
public interface IMediaServerIntegration
{
    /// <summary>
    /// Get the slug of the type of the media server.
    /// </summary>
    string MediaServerType { get; }

    /// <summary>
    /// Synchronize a library with the media server.
    /// </summary>
    /// <param name="libraryId">The unique id of the library to synchronize.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the sync succeeded; otherwise <see langword="false"/>.</returns>
    Task<bool> SyncLibraryAsync(Guid libraryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes metadata for a content hierarchy item to the media server.
    /// </summary>
    /// <param name="contentHierarchyId">The unique id of the content hierarchy item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the metadata was pushed; otherwise <see langword="false"/>.</returns>
    Task<bool> PushMetadataAsync(Guid contentHierarchyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes a library on the media server.
    /// </summary>
    /// <param name="libraryId">The unique id of the library to refresh.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the refresh was triggered; otherwise <see langword="false"/>.</returns>
    Task<bool> RefreshLibraryAsync(Guid libraryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the connection to the media server.
    /// </summary>
    /// <param name="settings">Provider-specific configuration to use for the test (host url, api key, etc.).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="ConnectionTestResult"/> describing success or failure with a message.</returns>
    Task<ConnectionTestResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);
}
