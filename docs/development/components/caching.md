# Cache providers

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

Magpie services and extensions get their caches from an `ICacheFactory`. Implement it when you want to change how caching works for the whole installation. It [replaces the host's factory](README.md#replacement-components). If you only need to cache your own extension's data, [use the existing factory](../using-built-in-services.md#using-shared-facilities) instead.

A consumer asks the factory for a cache by name and supplies a [`CachePolicy`](../../../src/Magpie.Sdk/Caching/CachePolicy.cs). `GetOrCreate` returns a named cache shared across scopes, and `CreateScoped` returns a private one for the current scope. A replacement keeps both behaviors.

Put shared storage in a separate singleton that the factory receives, because shared caches outlive any one scope. Register the factory as [scoped](../using-built-in-services.md#service-lifetime-and-ownership) in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point). The host registers its default only when no extension does.

```csharp
services.AddSingleton<ExampleCacheStore>();
services.AddScoped<ICacheFactory, ExampleCacheFactory>();
```

Implement `IDisposable` on the factory, so it can dispose the private caches it created when its scope ends. It must not dispose the shared storage. The built-in cache records the `magpie.cache.hit` and `magpie.cache.miss` [metrics](../../reference/telemetry.md#metadata-media-servers-data-and-events), and a replacement does not.

## Preserving cache behavior

`GetOrCreate` calls the policy factory only when the named cache does not exist yet, and later callers get the existing cache with its first policy. With no policy, use the default: 15 minutes of freshness, 15 minutes of retention, 1000 entries, and optimistic population.

Honor the policy's freshness, retention, entry limit, and concurrency mode. `Timeout.InfiniteTimeSpan` means no expiry. Reject other zero or negative lifetimes. `TryGet` returns only fresh values, and a value is removed at the earlier of its freshness and retention times. When the cache is over its entry limit, the built-in cache evicts the least recently used entry, and a successful `TryGet` counts as a use.

Caches are per process. The contract does not ask for distributed storage, but a provider that adds it must keep the local behavior.

In optimistic mode, concurrent callers can populate the same key. In pessimistic mode, they share one population per key, and when one caller cancels, only that caller stops waiting. Do not store a failed or cancelled population.

The optimistic path of `GetOrAddAsync` looks like this inside a class implementing `ICache`:

```csharp
private async ValueTask<T> PopulateOptimisticAsync<T>(
    string key,
    Func<CancellationToken, ValueTask<T>> valueFactory,
    CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    if (TryGet<T>(key, out var cached))
    {
        return cached!;
    }

    var value = await valueFactory(cancellationToken).ConfigureAwait(false);
    cancellationToken.ThrowIfCancellationRequested();
    Set(key, value);
    return value;
}
```

Use the host's `TimeProvider` for expiration. [MemoryCacheStore](../../../src/Magpie.Core/Caching/MemoryCacheStore.cs) is the behavior reference, and the exact contracts are [ICacheFactory](../../../src/Magpie.Sdk/Caching/ICacheFactory.cs) and [ICache](../../../src/Magpie.Sdk/Caching/ICache.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
