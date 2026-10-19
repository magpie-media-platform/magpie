# UI assets

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

An asset provider gives the browser the scripts, stylesheets, and link resources an extension needs. `IAssetProvider.ListAssets` declares them, and `GetAssetStreamAsync` opens the embedded content when the browser asks for it. The browser loads the declarations from the UI manifest, which lists all resources and contributions. A [UI contribution](ui-contributions.md) that refers to a custom element requires one, because the asset supplies the script that registers it. Assets are [additive](README.md#additional-components).

## Embedding and serving the Scratch script

First create `Assets/Widget.js` from the [UI contribution example](ui-contributions.md#adding-an-element-to-library-actions), and add it to the Scratch project file:

```xml
<ItemGroup>
  <EmbeddedResource Include="Assets/Widget.js" LogicalName="Scratch.Widget.js" />
</ItemGroup>
```

Then add the provider as `ScratchAssets.cs`:

```csharp
using Magpie.Models.Enums;
using Magpie.Models.UI;
using Magpie.Sdk.UI;

namespace Magpie.Extensions.Scratch;

internal sealed class ScratchAssets : IAssetProvider
{
    /// <inheritdoc />
    public IEnumerable<Asset> ListAssets()
    {
        yield return new()
        {
            Id = new Guid("802763b3-6553-465a-9c92-e957081faeb9"),
            ExtensionId = ScratchExtension.ExtensionId,
            Name = "Scratch widget script",
            Kind = AssetKind.Script,
            RelativePath = "widget.js",
            Priority = 100
        };
    }

    /// <inheritdoc />
    public Task<Stream?> GetAssetStreamAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stream = string.Equals(relativePath, "widget.js", StringComparison.Ordinal)
            ? typeof(ScratchAssets).Assembly.GetManifestResourceStream("Scratch.Widget.js")
            : null;
        return Task.FromResult(stream);
    }
}
```

Register it in `ScratchExtension.RegisterServices`, keeping the UI-provider registration from the previous page. The entry-point file needs `Magpie.Sdk.UI` and `Microsoft.Extensions.DependencyInjection`.

```csharp
services.AddSingleton<IAssetProvider, ScratchAssets>();
```

The relative path identifies the HTTP resource, and it can differ from both the source filename and the embedded resource name. The provider maps all three. A request to `/api/ui/assets/{extensionId}/widget.js` needs [viewer access](../../reference/roles.md) and asks the provider for a new stream, which the response then owns. Returning the same stream twice lets one request close or advance another's content.

Return null for unknown paths, and never turn the requested path into arbitrary file access or an unchecked assembly-resource lookup. Before the provider is asked, the host rejects rooted paths, `..` segments, and colons with 400. If the provider throws, the host logs it and returns 404. If `ListAssets` throws, the host logs a warning and leaves that provider's assets out of the manifest.

The browser accepts a relative path only when each `/`-separated segment starts with a letter or digit and contains only letters, digits, spaces, and `. _ ~ @ + ( ) -`, without starting or ending in a space. The path must not start with `/` or contain `\`, `?`, `#`, `:`, or an empty, `.`, or `..` segment. A path that breaks these rules stops [extension loading in the browser](#load-order-and-browser-context).

## Ownership and resource kind

Keep asset IDs and the owning extension ID stable. Use one asset provider per extension ID: the host takes the first provider that declares any asset for that ID and sends all requested paths to it, so paths split across providers never reach the later ones.

Set exactly one of `RelativePath` and `Url`. Use a relative path for content the provider serves, and `Url` for a remote resource the browser can reach.

| Kind | Use |
| --- | --- |
| `Script` | JavaScript that executes in the application page. |
| `Style` | CSS applied to the page. |
| `Link` | A link resource with an explicit `LinkRel`, such as an icon or preconnect declaration. |

For remote scripts and styles, `Integrity` supplies a subresource-integrity hash, and browser origin rules still apply. A script has access to the application page and the browser session, so ship only the code and data the script requires.

## Load order and browser context

The loader sorts assets by ascending `Priority`, awaits each load before starting the next, and skips repeated resource URLs. If scripts depend on each other, set explicit priorities. Make registration safe to run again, because the loader retries a failed load.

If an asset fails to load or its script throws, the loader stops and loads no later assets. No slot then shows any extension's contributions, only its built-in content. The browser retries the whole load every 3 seconds, up to 10 times, running the scripts again each time. After the last retry the page shows **Extensions could not load** with the error and a **Retry extensions** button.

For protected relative scripts and stylesheets, the loader fetches the source through the [authenticated API](../../reference/api.md#authentication-and-request-format) and inserts inline elements. While a script runs, `document.currentScript.magpieExtensionContext` provides:

- `apiBaseUrl` and `extensionId` for the current asset.
- `fetch(path, options)` for authenticated API requests. The path is relative to `apiBaseUrl`, for example `/example/ping`, not `/api/example/ping`. An object `body` is sent as JSON, and the promise rejects when the response status isn't successful.
- `loadAssetText(relativePath)` for another protected asset owned by the extension.
- `waitUntil(promise)` to include asynchronous initialization in this asset's load result.

The loader removes the property after the script runs, so capture the context during execution if later callbacks need it. Call `waitUntil` synchronously during that execution, since a later call throws. Remote scripts don't get this context.

The Scratch script has no asynchronous setup and needs none of this. The [Jellyfin asset provider](../../../src/Magpie.Extensions.Jellyfin/JellyfinAssetProvider.cs) and its [assets](../../../src/Magpie.Extensions.Jellyfin/Assets) are a larger example.

See [IAssetProvider](../../../src/Magpie.Sdk/UI/IAssetProvider.cs), [Asset](../../../src/Magpie.Models/UI/Asset.cs), and the [browser loader](../../../ui/src/extensions/ExtensionLoader.ts). For appearance choices, use a [theme provider](themes.md).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
