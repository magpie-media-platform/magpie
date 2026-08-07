using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Sdk.Integrations;

/// <summary>
/// A media server integration that can refresh upstream libraries.
/// </summary>
public interface IRefreshableMediaServerIntegration
{
    /// <summary>
    /// Refreshes content on the configured media server.
    /// </summary>
    /// <param name="server">The configured media server</param>
    /// <param name="contentExternalIds">The external ids of the content, keyed by source slug</param>
    /// <param name="contentType">The content type slug</param>
    /// <param name="rootFolderPath">The effective path of the content</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the server starts a refresh.</returns>
    Task<bool> RefreshForContentAsync(
        MediaServer server,
        IReadOnlyDictionary<string, string> contentExternalIds,
        string contentType,
        string rootFolderPath,
        CancellationToken cancellationToken = default);
}
