using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Integrations;

/// <summary>
/// Integration with an external download client.
/// </summary>
public interface IDownloadClientIntegration
{
    /// <summary>
    /// The provider type of the download client.
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// The supported protocols of the download client.
    /// </summary>
    IEnumerable<string> SupportedProtocols { get; }

    /// <summary>
    /// Adds a download to the client.
    /// </summary>
    /// <param name="release">The release to download.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<DownloadInfo> DownloadReleaseAsync(Release release, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the state of a download.
    /// </summary>
    /// <param name="externalId">The external id of the download.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result containing the download status on success, or an error on failure.</returns>
    Task<DownloadInfo> GetDownloadAsync(string externalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a download from the client.
    /// </summary>
    /// <param name="downloadId">The unique id of the download.</param>
    /// <param name="deleteFiles">Whether to delete the downloaded files.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result indicating success or failure with an error reason.</returns>
    Task<bool> RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the connection to the download client.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
