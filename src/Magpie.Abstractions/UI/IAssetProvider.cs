using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.UI;

namespace Magpie.Abstractions.UI;

/// <summary>
/// Provides UI assets from an extension.
/// </summary>
public interface IAssetProvider
{
    /// <summary>
    /// List the asset definitions provided by the extension.
    /// </summary>
    IEnumerable<Asset> ListAssets();

    /// <summary>
    /// Gets a stream for an embedded asset.
    /// </summary>
    /// <param name="relativePath">The relative path of the asset.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The asset stream, or null if the asset does not exist.</returns>
    Task<Stream?> GetAssetStreamAsync(string relativePath, CancellationToken cancellationToken = default);
}
