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
    /// <returns>The current state of the download.</returns>
    Task<DownloadInfo> GetDownloadAsync(string externalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a download from the client.
    /// </summary>
    /// <param name="downloadId">The unique id of the download.</param>
    /// <param name="deleteFiles">Whether to delete the downloaded files.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the download was removed; otherwise <see langword="false"/>.</returns>
    Task<bool> RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses an in-flight download.
    /// </summary>
    /// <param name="externalId">The external id of the download in the client.</param>
    /// <param name="settings">Provider-specific configuration (host, port, api key, etc.).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the download was paused (or already paused); false on failure.</returns>
    Task<bool> PauseDownloadAsync(
        string externalId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a paused download.
    /// </summary>
    /// <param name="externalId">The external id of the download in the client.</param>
    /// <param name="settings">Provider-specific configuration (host, port, api key, etc.).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the download was resumed (or already running); false on failure.</returns>
    Task<bool> ResumeDownloadAsync(
        string externalId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the connection to the download client.
    /// </summary>
    /// <param name="settings">Provider-specific configuration to use for the test (host, port, api key, etc.).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="ConnectionTestResult"/> describing success or failure with a message.</returns>
    Task<ConnectionTestResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);
}
