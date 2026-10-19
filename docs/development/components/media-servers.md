# Media server integrations

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

A media server integration connects Magpie to a media server such as Jellyfin, so Magpie can ask it to refresh a library after files change and link from a title to its page on the server. You write one to support a server Magpie doesn't already know. The connection itself, with its address and credentials, is a [configured server](../../administration/storage-and-services.md#service-roles). The integration supplies the behavior behind it.

## The contracts

The base `IMediaServerIntegration` supplies provider identity, a settings schema, the supported [content types](media-types.md), and a connection test. An empty `SupportedContentTypes` list means all content types. [Optional capabilities](../../reference/extensions.md#optional-capabilities) add refreshes and links. Implement all of them on one class.

| Interface | Host request | Result |
| --- | --- | --- |
| `IMediaServerIntegration` | Test the connection with its settings | A structured configuration test result |
| `IRefreshableMediaServerIntegration` | Refresh for content, [external IDs](metadata-providers.md#following-a-content-refresh), type, and effective path | Whether the server starts a refresh |
| `ILinkableMediaServerIntegration` | Find a confirmed item and build its link | An absolute item URI, or null |
| `IValidatesIntegrationSettings` | Validate provider settings | Settings validation for the connection |

For a failed test, set `FailureKind`, which sets the health of the configured server as described in [Reporting test failures](indexers.md#reporting-test-failures). The refresh job skips an `Unhealthy` server.

Register the class through the base interface in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point). The contracts are in `Magpie.Sdk.Integrations`.

```csharp
services.AddScoped<IMediaServerIntegration, ExampleMediaServer>();
```

Providers are [additive](README.md#additional-components) by type. Duplicate provider types produce a warning, and the [last registration](README.md#registration-order) wins. Connection tests and deep links treat types that differ only by case as duplicates, but the refresh job matches types case-sensitively, so keep the spelling consistent. One [scoped](../using-built-in-services.md#service-lifetime-and-ownership) integration can serve several configured servers, so use the server and settings from the current call.

The [Jellyfin implementation](../../../src/Magpie.Extensions.Jellyfin/JellyfinMediaServerIntegration.cs) shows settings, library mappings, remote lookup, refresh, and links together.

## Refreshing

The **Media server refresh** job runs 30 seconds after the media files of a content item change, and on a manual request. It calls `RefreshForContentAsync` for each enabled server that isn't `Unhealthy`, skipping servers whose configured content types, or whose integration's `SupportedContentTypes`, exclude the content's type.

For a content item, the integration receives its external IDs, its content type, and its effective path in the `rootFolderPath` parameter. Use the path and the configured mappings to choose the library, since a path visible to Magpie can mean something different on the server. In a manual run without a content item, all three are empty, and the call should be a full refresh.

`RefreshForContentAsync` returns true when the server starts a refresh. The server can keep scanning after that, so the title may not be there yet. A false result is logged as the server not triggering a refresh. If the method throws, the job logs the exception and continues with the next server.

## Linking

Magpie calls `BuildItemDeepLinkAsync` only for content that has external IDs. It receives the configured server, the external IDs, the content type, the root folder path, and the display title. Use the identities and the library mapping to confirm the remote item, because a title-only guess can open a different item with the same name. Once a lookup has confirmed `itemId`, building the link for Jellyfin looks like this:

```csharp
if (string.IsNullOrWhiteSpace(itemId))
{
    return null;
}

var detailsUrl = baseUrl.TrimEnd('/') + "/web/index.html#!/details?id="
    + Uri.EscapeDataString(itemId);
return Uri.TryCreate(detailsUrl, UriKind.Absolute, out var detailsUri)
    ? detailsUri
    : null;
```

This belongs in a method that returns `Uri?`, and `baseUrl` is the configured Jellyfin address. Use the real item route for another server product. Return null if no item can be confirmed, and Magpie shows no link for that server. If the remote request fails, throw. Magpie logs a warning and omits the link.

Pass cancellation through remote calls, and use the [built-in HTTP and logging facilities](../using-built-in-services.md#using-shared-facilities).

Exact contracts: [base integration](../../../src/Magpie.Sdk/Integrations/IMediaServerIntegration.cs), [refresh](../../../src/Magpie.Sdk/Integrations/IRefreshableMediaServerIntegration.cs), and [links](../../../src/Magpie.Sdk/Integrations/ILinkableMediaServerIntegration.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
