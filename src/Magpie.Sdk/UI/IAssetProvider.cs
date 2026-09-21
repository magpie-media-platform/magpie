using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.UI;

namespace Magpie.Sdk.UI;

/// <summary>
/// The UI assets that an extension embeds.
/// </summary>
public interface IAssetProvider
{
    /// <summary>
    /// List the asset definitions provided by the extension.
    /// </summary>
    IEnumerable<Asset> ListAssets();

    /// <summary>
    /// A stream for an embedded asset.
    /// </summary>
    /// <param name="relativePath">The relative path of the asset</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The asset stream, or null if the asset does not exist</returns>
    Task<Stream?> GetAssetStreamAsync(string relativePath, CancellationToken cancellationToken = default);
}
