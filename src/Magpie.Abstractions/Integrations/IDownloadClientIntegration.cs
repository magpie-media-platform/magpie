using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Downloads;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Integrations;

/// <summary>
/// Integration with an external download client.
/// </summary>
public interface IDownloadClientIntegration
{
    /// <summary>
    /// Gets the slug of the type of the download client integration.
    /// </summary>
    public string DownloadClientType { get; }

    /// <summary>
    /// Adds a download to the client.
    /// </summary>
    /// <param name="downloadUrl">The download URL or magnet URI.</param>
    /// <param name="category">Optional category for the download.</param>
    /// <param name="destinationPath">Optional destination path for the download.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The result containing the unique identifier assigned to the download on success, or an error on failure.
    /// </returns>
    Task<Result<string>> AddDownloadAsync(
        Uri downloadUrl,
        string? category,
        string? destinationPath,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets the status of a download.
    /// </summary>
    /// <param name="downloadId">The unique identifier of the download.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result containing the download status on success, or an error on failure.</returns>
    Task<Result<DownloadStatus>> GetDownloadStatusAsync(
        string downloadId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets all active downloads.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result containing a list of active downloads on success, or an error on failure.</returns>
    Task<Result<IReadOnlyList<DownloadStatus>>> GetActiveDownloadsAsync(
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Removes a download from the client.
    /// </summary>
    /// <param name="downloadId">The unique identifier of the download.</param>
    /// <param name="deleteFiles">Whether to delete the downloaded files.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result indicating success or failure with an error reason.</returns>
    Task<Result<bool>> RemoveDownloadAsync(
        string downloadId,
        bool deleteFiles,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Tests the connection to the download client.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result indicating success or failure with an error reason.</returns>
    Task<Result<bool>> TestConnectionAsync(CancellationToken cancellationToken);
}
