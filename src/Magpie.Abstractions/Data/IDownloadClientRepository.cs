using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Downloads;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing download client configurations.
/// </summary>
public interface IDownloadClientRepository :
    ICrudRepository<Guid, DownloadClient>,
    IPatchRepository<Guid, DownloadClient, DownloadClientPatch>
{
    /// <summary>
    /// Get a download client by name.
    /// </summary>
    /// <param name="name">The client name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<DownloadClient?>> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all download clients for a provider.
    /// </summary>
    /// <param name="provider">The provider identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<DownloadClient>>> GetByProviderAsync(
        string provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all download clients with a specific status.
    /// </summary>
    /// <param name="status">The status to filter by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<DownloadClient>>> GetByStatusAsync(
        DownloadClientStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all enabled download clients.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<DownloadClient>>> GetEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the status and last checked time for a download client.
    /// </summary>
    /// <param name="id">The client identifier.</param>
    /// <param name="status">The new status.</param>
    /// <param name="lastChecked">The time the client was checked.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<bool>> UpdateStatusAsync(
        Guid id,
        DownloadClientStatus status,
        DateTimeOffset lastChecked,
        CancellationToken cancellationToken = default);
}
