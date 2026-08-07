using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Sdk.Integrations;

/// <summary>
/// A download client that can start a transfer immediately.
/// </summary>
public interface IForcableDownloadClient
{
    /// <summary>
    /// Starts a download immediately and ignores the client queue and rate limits.
    /// </summary>
    /// <param name="externalId">The external id of the download in the client</param>
    /// <param name="settings">The settings of the configured provider</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> for an accepted request, otherwise <see langword="false"/>.</returns>
    Task<bool> ForceStartDownloadAsync(
        string externalId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);
}
