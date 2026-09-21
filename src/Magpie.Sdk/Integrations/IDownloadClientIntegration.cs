using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Models.Integrations;

namespace Magpie.Sdk.Integrations;

/// <summary>
/// The integration contract for an external download client.
/// </summary>
public interface IDownloadClientIntegration
{
    /// <summary>
    /// The provider type of the download client.
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// The download protocols that the client supports.
    /// </summary>
    IEnumerable<string> SupportedProtocols { get; }

    /// <summary>
    /// The schema of the client settings.
    /// </summary>
    IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; }

    /// <summary>
    /// Adds a download to the client.
    /// </summary>
    /// <param name="release">The release to download</param>
    /// <param name="settings">The settings of the configured provider</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The added download.</returns>
    Task<DownloadInfo> DownloadReleaseAsync(
        Release release,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a download from the client.
    /// </summary>
    /// <param name="downloadId">The id of the download to remove</param>
    /// <param name="deleteFiles">Whether the client also deletes the downloaded files</param>
    /// <param name="settings">The settings of the configured provider</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>
    /// <see langword="true"/> when the download was removed, <see langword="false"/> when it was not found.
    /// </returns>
    Task<bool> RemoveDownloadAsync(
        string downloadId,
        bool deleteFiles,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the connection to the download client.
    /// </summary>
    /// <param name="settings">The settings of the configured provider</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The result of the connection test.</returns>
    Task<ConfigurationTestResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a download to the client for a release grab.
    /// </summary>
    /// <param name="release">The release to download</param>
    /// <param name="context">The context of the release grab</param>
    /// <param name="settings">The settings of the configured provider</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The added download.</returns>
    Task<DownloadInfo> DownloadReleaseAsync(
        Release release,
        ReleaseGrabContext context,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        return DownloadReleaseAsync(release, settings, cancellationToken);
    }

    /// <summary>
    /// A download by its external id.
    /// </summary>
    /// <param name="externalId">The external id of the download</param>
    /// <param name="settings">The settings of the configured provider</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The download.</returns>
    Task<DownloadInfo> GetDownloadAsync(
        string externalId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Multiple downloads by their external ids.
    /// </summary>
    /// <param name="externalIds">The external ids of the downloads</param>
    /// <param name="settings">The settings of the configured provider</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The downloads and the failures, keyed by external id.</returns>
    async Task<BatchDownloadStatusResult> GetDownloadsAsync(
        IEnumerable<string> externalIds,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        if (externalIds is null)
        {
            throw new ArgumentNullException(nameof(externalIds));
        }

        var statuses = new Dictionary<string, DownloadInfo>();
        var failures = new Dictionary<string, Exception>();
        foreach (var externalId in externalIds)
        {
            if (string.IsNullOrEmpty(externalId))
            {
                continue;
            }

            if (statuses.ContainsKey(externalId) || failures.ContainsKey(externalId))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = await GetDownloadAsync(externalId, settings, cancellationToken)
                    .ConfigureAwait(false);
                statuses[externalId] = info;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures[externalId] = ex;
            }
        }

        return new BatchDownloadStatusResult(statuses, failures);
    }
}
