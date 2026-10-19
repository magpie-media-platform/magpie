# Indexer integrations

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

An indexer integration turns a Magpie [release search](../../using/search-and-download.md) into requests to an [external indexer](../../administration/storage-and-services.md#service-roles) and returns downloadable release candidates. Magpie picks the [download client](download-clients.md) and imports the files. Implement `IIndexerIntegration` to support a service directly. If an external catalog supplies many indexer definitions, use an [indexer feed](indexer-feeds.md) with a [runtime factory](runtime-indexers.md) instead.

The integration declares a stable `ProviderType`, a display name, a [settings schema](settings.md), supported [content types](media-types.md), and [search capabilities](#declaring-only-executable-searches). Magpie stores configured connections separately and passes the selected connection's settings with a `ReleaseSearchRequest` at search time. `SupportedContentTypes` applies only to feed-supplied definitions with the same provider type. A connection created directly stores no content types and receives every search, so use `SearchFacets` to refuse what the source cannot serve.

One [scoped](../using-built-in-services.md#service-lifetime-and-ownership) integration can receive concurrent calls for different connections. Read credentials and endpoints from each call's settings, because credentials in mutable instance fields can leak between accounts. Register the class in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point):

```csharp
services.AddScoped<IIndexerIntegration, CatalogIndexer>();
```

The contract is in `Magpie.Sdk.Integrations`, and the [Cardigann integration](../../../src/Magpie.Extensions.Cardigann/CardigannIndexerIntegration.cs) shows search translation, authentication, and result parsing. Providers are [additive](README.md#additional-components) by type. With several registrations of one `ProviderType`, Magpie uses the last, and a matching direct integration wins over a stored definition's runtime factory.

## Declaring only executable searches

`SearchFacets` lists the parameter combinations the source accepts together. This integration supports an IMDb-ID search and a keyword search with season and episode:

```csharp
public IReadOnlyList<IReadOnlyCollection<string>> SearchFacets =>
[
    new[] { "imdbid" },
    new[] { "q", "season", "episode" }
];

public bool SupportsKeywordSearch => true;
```

Match these to the source's real API and your translation. An [identity source](metadata-providers.md#following-a-content-refresh) name can differ from the parameter name, so an `imdb` identity may become an `imdbid` parameter. For an identifier request, read `IdSearchParameter` and `IdSearchValue` rather than using the source slug. `ReleaseSearchRequest` also has title, year, criteria, categories, an interactive flag, and a result limit. Where target facts come from is covered in [Content facts](content-facts.md).

## Returning releases and distinguishing failure

Map a response to `Release` values with the reported title, protocol, stable external identity, and known size. Set `InfoUrl` to the payload link, a magnet URI or an http or https URL, which download clients read. Return an empty sequence for a successful search with no candidates, and throw for authentication, transport, or parsing failures. Magpie then marks the connection `Degraded` and skips it for the rest of that search. An empty result on failure looks like a source with nothing to offer.

## Reporting test failures

`TestConnectionAsync` returns a `ConfigurationTestResult`. On failure, set `Message` and `FailureKind`.

| `FailureKind` | Result |
| --- | --- |
| `Auth`, `NotFound`, or `Fatal` | Magpie marks the connection `Unhealthy`. |
| `Transient` | Magpie marks the connection `Degraded`, and `Unhealthy` after 3 consecutive failed tests. |

Use `Transient` for failures that can clear without a configuration change. Magpie skips an `Unhealthy` connection in searches until a test succeeds, and a successful test marks it `Healthy`. Download clients and media servers follow the same rules. Optional [`IValidatesIntegrationSettings`](../../reference/extensions.md#optional-capabilities) can reject bad settings before any network work.

## Fetching protected payloads

Override `FetchPayloadAsync` when downloading a payload needs the indexer's authenticated session. The default returns null, meaning the integration has no session-specific fetch. Magpie does not call it directly. A download client calls `IIndexerService.FetchPayloadAsync` with the connection ID from `ReleaseGrabContext`, and if your method throws, that call reports a transient `ReleasePayloadRejectedException`.

Use managed [HTTP clients and logging](../using-built-in-services.md#using-shared-facilities), pass cancellation through searches, tests, and payload requests, and keep credentials out of logs.

## Verifying the complete path

Interactive searches always query the indexer. An automatic search reuses the result of an identical request for [`Search:CoalescingWindow`](../../reference/configuration.md#background-work), 15 minutes by default. To repeat an automatic search straight away, run the **Release search item** job with `force` set to `true`.

See [IIndexerIntegration](../../../src/Magpie.Sdk/Integrations/IIndexerIntegration.cs), [ReleaseSearchRequest](../../../src/Magpie.Models/ReleaseSearchRequest.cs), and [Release](../../../src/Magpie.Models/Data/Release.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
