using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Memory;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed class QueryCache : IQueryCache
{
    private static readonly TimeSpan OrdinaryPageTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AdvancedPageTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan WorkTtl = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan AuthorTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromMinutes(2);

    private readonly object _gate = new();
    private readonly MemoryCache _pages = new(new MemoryCacheOptions { SizeLimit = 256 });
    private readonly MemoryCache _resolutions = new(new MemoryCacheOptions { SizeLimit = 512 });
    private readonly MemoryCache _authors = new(new MemoryCacheOptions { SizeLimit = 256 });
    private readonly MemoryCache _editions = new(new MemoryCacheOptions { SizeLimit = 256 });
    private readonly MemoryCache _negativeIdentities = new(new MemoryCacheOptions { SizeLimit = 128 });
    private readonly Dictionary<string, Task<SourcePage>> _pageInflight = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<WorkResolution>> _resolutionInflight = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<AuthorLookup>> _authorInflight = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<EditionLookup>> _editionInflight = new(StringComparer.Ordinal);

    public Task<SourcePage> GetSourcePageAsync(
        string key,
        bool exactClass,
        Func<CancellationToken, Task<SourcePage>> factory,
        CancellationToken cancellationToken)
    {
        return GetOrCreateAsync(
            key,
            exactClass ? OrdinaryPageTtl : AdvancedPageTtl,
            _pages,
            _pageInflight,
            factory,
            cancellationToken);
    }

    public Task<WorkResolution> GetResolutionAsync(
        string key,
        Func<CancellationToken, Task<WorkResolution>> factory,
        CancellationToken cancellationToken)
    {
        var negativeKey = string.Concat("work|", key);
        if (_negativeIdentities.TryGetValue(negativeKey, out _))
        {
            return Task.FromResult(new WorkResolution { Kind = WorkResolutionKind.NotFound });
        }

        return GetOrCreateAsync(
            key,
            WorkTtl,
            _resolutions,
            _resolutionInflight,
            async operationToken =>
            {
                var value = await factory(operationToken).ConfigureAwait(false);
                if (value.Kind == WorkResolutionKind.NotFound)
                {
                    Remember(_negativeIdentities, negativeKey, NegativeTtl);
                }

                return value;
            },
            cancellationToken,
            shouldCache: value => value.Kind != WorkResolutionKind.NotFound);
    }

    public Task<AuthorLookup> GetAuthorAsync(
        string key,
        Func<CancellationToken, Task<AuthorLookup>> factory,
        CancellationToken cancellationToken)
    {
        var negativeKey = string.Concat("author|", key);
        if (_negativeIdentities.TryGetValue(negativeKey, out _))
        {
            return Task.FromResult(new AuthorLookup { Found = false });
        }

        return GetOrCreateAsync(
            key,
            AuthorTtl,
            _authors,
            _authorInflight,
            async operationToken =>
            {
                var value = await factory(operationToken).ConfigureAwait(false);
                if (!value.Found)
                {
                    Remember(_negativeIdentities, negativeKey, NegativeTtl);
                }

                return value;
            },
            cancellationToken,
            shouldCache: value => value.Found);
    }

    public Task<EditionLookup> GetEditionAsync(
        string key,
        Func<CancellationToken, Task<EditionLookup>> factory,
        CancellationToken cancellationToken)
    {
        var negativeKey = string.Concat("edition|", key);
        if (_negativeIdentities.TryGetValue(negativeKey, out _))
        {
            return Task.FromResult(new EditionLookup { Found = false });
        }

        return GetOrCreateAsync(
            key,
            AuthorTtl,
            _editions,
            _editionInflight,
            async operationToken =>
            {
                var value = await factory(operationToken).ConfigureAwait(false);
                if (!value.Found)
                {
                    Remember(_negativeIdentities, negativeKey, NegativeTtl);
                }

                return value;
            },
            cancellationToken,
            shouldCache: value => value.Found);
    }

    private void Remember(MemoryCache cache, string key, TimeSpan ttl)
    {
        Remember(cache, key, true, ttl);
    }

    private void Remember(MemoryCache cache, string key, object value, TimeSpan ttl)
    {
        cache.Set(key, value, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = ttl });
    }

    private async Task<T> AwaitAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled || task.IsCompleted)
        {
            return await task.ConfigureAwait(false);
        }

        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => cancelled.TrySetResult(true));
        var completed = await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false);
        if (completed != task)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return await task.ConfigureAwait(false);
    }

    private Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        MemoryCache cache,
        Dictionary<string, Task<T>> inflight,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken,
        Func<T, bool>? shouldCache = null)
        where T : class
    {
        Task<T> task;
        lock (_gate)
        {
            if (cache.TryGetValue(key, out T? cached) && cached is not null)
            {
                return Task.FromResult(cached);
            }

            if (!inflight.TryGetValue(key, out task!))
            {
                var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                task = completion.Task;
                inflight.Add(key, task);
                _ = PopulateAsync(key, ttl, cache, inflight, factory, shouldCache, completion);
            }
        }

        return AwaitAsync(task, cancellationToken);
    }

    private async Task PopulateAsync<T>(
        string key,
        TimeSpan ttl,
        MemoryCache cache,
        Dictionary<string, Task<T>> inflight,
        Func<CancellationToken, Task<T>> factory,
        Func<T, bool>? shouldCache,
        TaskCompletionSource<T> completion)
        where T : class
    {
        try
        {
            var value = await factory(default).ConfigureAwait(false);
            if (shouldCache?.Invoke(value) != false)
            {
                Remember(cache, key, value, ttl);
            }

            completion.TrySetResult(value);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        finally
        {
            lock (_gate)
            {
                inflight.Remove(key);
            }
        }
    }
}
