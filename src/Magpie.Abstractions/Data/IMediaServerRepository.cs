using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.MediaServers;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing media server configurations.
/// </summary>
public interface IMediaServerRepository :
    ICrudRepository<Guid, MediaServer>,
    IPatchRepository<Guid, MediaServer, MediaServerPatch>
{
    /// <summary>
    /// Get a media server by name.
    /// </summary>
    /// <param name="name">The server name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<MediaServer?>> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all media servers for a provider.
    /// </summary>
    /// <param name="provider">The provider identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<MediaServer>>> GetByProviderAsync(
        string provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all media servers with a specific status.
    /// </summary>
    /// <param name="status">The status to filter by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<MediaServer>>> GetByStatusAsync(
        MediaServerStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all enabled media servers.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<MediaServer>>> GetEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the status and last checked time for a media server.
    /// </summary>
    /// <param name="id">The server identifier.</param>
    /// <param name="status">The new status.</param>
    /// <param name="lastChecked">The time the server was checked.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<bool>> UpdateStatusAsync(
        Guid id,
        MediaServerStatus status,
        DateTimeOffset lastChecked,
        CancellationToken cancellationToken = default);
}
