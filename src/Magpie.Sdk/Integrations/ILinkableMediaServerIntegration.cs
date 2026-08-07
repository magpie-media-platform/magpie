using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Sdk.Integrations;

/// <summary>
/// A media server integration that can create item deep links.
/// </summary>
public interface ILinkableMediaServerIntegration
{
    /// <summary>
    /// Creates a deep link to a confirmed media server item.
    /// </summary>
    /// <param name="server">The configured media server</param>
    /// <param name="externalIds">The external ids of the content, keyed by source slug</param>
    /// <param name="contentType">The content type slug</param>
    /// <param name="rootFolderPath">The path of the root folder that holds the content</param>
    /// <param name="title">The title to display</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The item URI, or <see langword="null"/> without a verified link.</returns>
    Task<Uri?> BuildItemDeepLinkAsync(
        MediaServer server,
        IReadOnlyDictionary<string, string> externalIds,
        string contentType,
        string rootFolderPath,
        string title,
        CancellationToken cancellationToken = default);
}
