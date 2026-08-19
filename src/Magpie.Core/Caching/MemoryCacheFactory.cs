using System;
using System.Collections.Generic;
using System.Threading;

using Magpie.Sdk.Caching;

namespace Magpie.Core.Caching;

internal sealed class MemoryCacheFactory(MemoryCacheStore store) : ICacheFactory, IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<MemoryCacheStore.Cache> _scopedCaches = [];
    private bool _disposed;

    /// <inheritdoc />
    public ICache GetOrCreate(string name, Func<CachePolicy>? createPolicy = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return store.GetOrCreate(name, createPolicy);
    }

    /// <inheritdoc />
    public ICache CreateScoped(string name, CachePolicy? policy = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var cache = store.CreateScoped(name, policy ?? new CachePolicy());
        lock (_lock)
        {
            if (_disposed)
            {
                cache.Dispose();
                throw new ObjectDisposedException(GetType().FullName);
            }

            _scopedCaches.Add(cache);
        }

        return cache;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var cache in _scopedCaches)
            {
                cache.Dispose();
            }

            _scopedCaches.Clear();
        }
    }
}
