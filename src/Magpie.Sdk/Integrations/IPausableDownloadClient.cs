using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Sdk.Integrations;

/// <summary>
/// A download client that can pause an in-flight transfer.
/// </summary>
public interface IPausableDownloadClient
{
    /// <summary>
    /// Pauses an in-flight download.
    /// </summary>
    /// <param name="externalId">The external id of the download in the client</param>
    /// <param name="settings">The settings of the configured provider</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>
    /// <see langword="true"/> when the client pauses the download or the download is already paused,
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> PauseDownloadAsync(
        string externalId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);
}
