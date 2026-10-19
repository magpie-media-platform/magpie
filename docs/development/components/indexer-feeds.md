# Indexer feeds

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

An indexer feed imports indexer definitions from an external catalog, so the list of available [indexers](indexers.md) can change without a new extension release. You write one by implementing `IIndexerFeedProvider` when such a catalog exists. The feed only supplies definitions describing which indexers exist and how they work. Connections hold the credentials and choices for an individual indexer, and an indexer integration or [runtime factory](runtime-indexers.md) performs the searches.

## Synchronization

The **Indexer feed sync item** [job](../../administration/jobs-and-automation.md#jobs-and-executions) runs when an administrator creates a feed, on an hourly scan (`0 * * * *`), and on request. It loads the `IndexerFeed`, picks the provider by `ProviderType`, and calls `SyncAsync`, which returns `Indexer` definitions. Magpie saves them, records the sync time, and writes an audit entry. For a disabled feed or one with no registered provider, the job does not call `SyncAsync` and completes with a progress message saying so, so read that message before assuming the catalog was empty.

A definition for a runtime-backed indexer looks like this, with `Magpie.Models.Data`. It belongs in your `SyncAsync` result, and Magpie writes the records, not you:

```csharp
var indexer = new Indexer
{
    ProviderType = "example-catalog-a",
    Name = "Example Catalog A",
    DefinitionType = "example-definition",
    SchemaData = definitionText,
    SupportedContentTypes = ["movie"]
};
```

`ProviderType` identifies the individual indexer across syncs. `DefinitionType` selects a runtime factory, and `SchemaData` is the definition that factory interprets, so a catalog can return many indexers with different provider types and one shared definition type. If a direct indexer integration matches `ProviderType`, Magpie replaces `SupportedContentTypes` with that integration's list before saving. The [Cardigann feed provider](../../../src/Magpie.Extensions.Cardigann/CardigannFeedProvider.cs) is a complete example, and its [registration](../../../src/Magpie.Extensions.Cardigann/CardigannExtension.cs) shows the dependencies.

## Returning a complete catalog or failing

A non-empty result is the retained catalog. Magpie updates matching definitions by provider type, adds new ones, and removes feed-owned indexers the result omits, while retained definitions keep their connection state. If a feed supplied A and B and the result contains only A, B is removed. So if a catalog request fails partway, do not return the pages you read so far. Throw instead. Magpie then keeps the existing definitions and the previous sync time, and the job runs again under its `Retry` failure policy. An empty result also keeps the existing definitions, but records the sync time.

Keep provider types stable. Changing one removes the old definition and adds a new one, while a display-name change keeps the identity.

## Registration

Register the provider in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point):

```csharp
services.AddSingleton<IIndexerFeedProvider, CatalogFeedProvider>();
```

The contract is in `Magpie.Sdk.Integrations`. A [singleton](../using-built-in-services.md#service-lifetime-and-ownership) works when all dependencies do, as Cardigann does, and it must keep each feed's settings local to its call and allow concurrent syncs. Providers are [additive](README.md#additional-components) by type, and the job logs duplicates and uses the [last registration](README.md#registration-order). `TestConnectionAsync` returns a Boolean, unlike an indexer integration's structured result. Pass cancellation through downloads and long parsing loops, and use [built-in facilities](../using-built-in-services.md#using-shared-facilities) for HTTP clients and logging.

See [IIndexerFeedProvider](../../../src/Magpie.Sdk/Integrations/IIndexerFeedProvider.cs), the [sync job](../../../src/Magpie.Core/Jobs/IndexerFeedSyncItemJob.cs), and [definition synchronization](../../../src/Magpie.Core/IndexerService.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
