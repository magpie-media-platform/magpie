using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Sdk.Integrations;

/// <summary>
/// A download client that can resume a paused transfer.
/// </summary>
public interface IResumableDownloadClient
{
    /// <summary>
    /// Resumes a paused download.
    /// </summary>
    /// <param name="externalId">The external id of the download in the client</param>
    /// <param name="settings">The settings of the configured provider</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>
    /// <see langword="true"/> when the client resumes the download or the download already runs,
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> ResumeDownloadAsync(
        string externalId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);
}
