using System;
using System.Threading;
using System.Threading.Tasks;

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
    /// <returns>The result indicating success or failure with an error reason.</returns>
    Task<bool> SyncLibraryAsync(Guid libraryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes metadata for a content hierarchy item to the media server.
    /// </summary>
    /// <param name="contentHierarchyId">The unique id of the content hierarchy item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result indicating success or failure with an error reason.</returns>
    Task<bool> PushMetadataAsync(Guid contentHierarchyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes a library on the media server.
    /// </summary>
    /// <param name="libraryId">The unique id of the library to refresh.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result indicating success or failure with an error reason.</returns>
    Task<bool> RefreshLibraryAsync(Guid libraryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the connection to the media server.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result indicating success or failure with an error reason.</returns>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
