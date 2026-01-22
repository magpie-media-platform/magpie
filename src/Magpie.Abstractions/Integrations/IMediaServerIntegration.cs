using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;

namespace Magpie.Abstractions.Integrations;

/// <summary>
/// Integration with an external media server.
/// </summary>
public interface IMediaServerIntegration
{
    /// <summary>
    /// Gets the slug of the type of the media server.
    /// </summary>k
    string MediaServerType { get; }

    /// <summary>
    /// Synchronizes a library with the media server.
    /// </summary>
    /// <param name="libraryId">The unique identifier of the library to synchronize.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result indicating success or failure with an error reason.</returns>
    Task<Result<bool>> SyncLibraryAsync(Guid libraryId, CancellationToken cancellationToken);

    /// <summary>
    /// Pushes metadata for a content hierarchy item to the media server.
    /// </summary>
    /// <param name="contentHierarchyId">The unique identifier of the content hierarchy item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result indicating success or failure with an error reason.</returns>
    Task<Result<bool>> PushMetadataAsync(Guid contentHierarchyId, CancellationToken cancellationToken);

    /// <summary>
    /// Refreshes a library on the media server.
    /// </summary>
    /// <param name="libraryId">The unique identifier of the library to refresh.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result indicating success or failure with an error reason.</returns>
    Task<Result<bool>> RefreshLibraryAsync(Guid libraryId, CancellationToken cancellationToken);

    /// <summary>
    /// Tests the connection to the media server.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result indicating success or failure with an error reason.</returns>
    Task<Result<bool>> TestConnectionAsync(CancellationToken cancellationToken);
}
