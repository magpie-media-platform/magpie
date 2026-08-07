using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Enums;
using Magpie.Models.UI;
using Magpie.Sdk.UI;

namespace Magpie.Extensions.Jellyfin;

internal sealed class JellyfinAssetProvider : IAssetProvider
{
    private static readonly Guid AssetId = new("C5809D04-A106-45AD-8A34-995E6AF53717");
    private const string AssetPath = "jellyfin-libraries.js";
    private const string ResourceName = "Magpie.Extensions.Jellyfin.Assets.jellyfin-libraries.js";

    public IEnumerable<Asset> ListAssets()
    {
        return
        [
            new Asset
            {
                Id = AssetId,
                ExtensionId = Constants.ExtensionId,
                Name = "Jellyfin library options",
                Kind = AssetKind.Script,
                RelativePath = AssetPath,
                Priority = 100
            }
        ];
    }

    public Task<Stream?> GetAssetStreamAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(relativePath, AssetPath, StringComparison.Ordinal))
        {
            return Task.FromResult<Stream?>(null);
        }

        var stream = typeof(JellyfinAssetProvider).Assembly.GetManifestResourceStream(ResourceName);
        return Task.FromResult(stream);
    }
}
