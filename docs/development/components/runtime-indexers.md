# Runtime indexer factories

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

A runtime factory creates an [indexer integration](indexers.md) from a stored definition, so a catalog can add or update indexers without a compiled class for each one. You write one to support a definition format. It works with the definitions an [indexer feed](indexer-feeds.md) supplies, and the object it returns implements the same indexer contract as a directly registered integration.

## When the factory is used

To resolve an indexer, Magpie first looks for a registered `IIndexerIntegration` with the same `ProviderType`, and the last matching one wins. If there is none, the indexer needs both `DefinitionType` and `SchemaData`. Magpie then picks the [last registered](README.md#registration-order) factory whose `DefinitionType` matches and calls `Create(indexer)`.

A factory identifies a definition format, and many stored indexers can share one. Keep each indexer's identity and capabilities in the integration you build.

## Creating the integration

Magpie calls `Create` every time it resolves the indexer: for each connection in each search, and for each test, settings-schema request, capability listing, and payload fetch. It doesn't keep the result, so keep `Create` cheap. It is synchronous with no cancellation token, so parse the stored data and construct the integration there, and do remote requests later in the integration's own asynchronous methods.

The [Cardigann factory](../../../src/Magpie.Extensions.Cardigann/CardigannRuntimeIntegrationFactory.cs) is a complete implementation. This is a simplified version of its construction path:

```csharp
if (string.IsNullOrWhiteSpace(indexer.SchemaData))
{
    throw new InvalidOperationException("The indexer definition is missing.");
}

var definition = loader.LoadString(indexer.SchemaData, indexer.Name);
return new CardigannIndexerIntegration(
    indexer.Id,
    definition,
    parser,
    sessions,
    logger);
```

`loader`, `parser`, `sessions`, and `logger` are Cardigann's own constructor-injected dependencies, not SDK types. The real factory also keeps up to 100 parsed definitions in memory for its lifetime, keyed by the definition text. Key any cache like this by the definition contents, not the indexer identity, or searches keep using the old definition after a feed changes `SchemaData`.

Another format needs its own parser and integration. Throw for a missing or invalid definition.

## Registering

Register the factory in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point). The interface is in `Magpie.Sdk.Integrations`.

```csharp
services.AddScoped<IRuntimeIndexerIntegrationFactory, DefinitionIndexerFactory>();
```

Its `DefinitionType` must match the feed's definitions, so choose a unique one. If two factories share a type, the last registration wins.

Dependency injection doesn't track or dispose the integration the factory returns. Use dependencies whose [lifetime](../using-built-in-services.md#service-lifetime-and-ownership) the host already manages, such as the host's HTTP client facilities, and give any other resource you own a clear disposal path.

See the [factory contract](../../../src/Magpie.Sdk/Integrations/IRuntimeIndexerIntegrationFactory.cs), [host resolution](../../../src/Magpie.Core/IndexerService.cs), and [Cardigann registration](../../../src/Magpie.Extensions.Cardigann/CardigannExtension.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
