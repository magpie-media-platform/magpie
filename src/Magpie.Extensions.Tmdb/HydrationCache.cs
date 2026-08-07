using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.MetadataSearch;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;

namespace Magpie.Extensions.Tmdb;

internal sealed class HydrationCache(IHostApplicationLifetime lifetime) : IHydrationCache, IDisposable
{
    private const int Capacity = 256;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private readonly MemoryCache _entries = new(new MemoryCacheOptions { SizeLimit = Capacity });
    private readonly Dictionary<HydrationKey, Task<MetadataSearchCandidate?>> _inflight = [];
    private readonly object _gate = new();

    public async Task<MetadataSearchCandidate?> GetOrCreateAsync<TState>(
        HydrationKey key,
        TState state,
        Func<TState, CancellationToken, Task<MetadataSearchCandidate?>> loader,
        CancellationToken cancellationToken)
    {
        if (_entries.TryGetValue(key, out MetadataSearchCandidate? cached))
        {
            return cached;
        }

        var task = default(Task<MetadataSearchCandidate?>)!;
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out cached))
            {
                return cached;
            }

            if (!_inflight.TryGetValue(key, out task!))
            {
                task = LoadAsync(key, state, loader);
                _inflight[key] = task;
            }
        }

        return await WaitAsync(task, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _entries.Dispose();

    private static async Task<MetadataSearchCandidate?> WaitAsync(
        Task<MetadataSearchCandidate?> task,
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled || task.IsCompleted)
        {
            return await task.ConfigureAwait(false);
        }

        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => cancelled.TrySetResult(true));
        if (task != await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false))
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return await task.ConfigureAwait(false);
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private async Task<MetadataSearchCandidate?> LoadAsync<TState>(
        HydrationKey key,
        TState state,
        Func<TState, CancellationToken, Task<MetadataSearchCandidate?>> loader)
    {
        await Task.Yield();
        try
        {
            var candidate = await loader(state, lifetime.ApplicationStopping).ConfigureAwait(false);
            _entries.Set(
                key,
                candidate,
                new MemoryCacheEntryOptions
                {
                    Size = 1,
                    AbsoluteExpirationRelativeToNow = Ttl
                });
            return candidate;
        }
        finally
        {
            lock (_gate)
            {
                _inflight.Remove(key);
            }
        }
    }

    internal readonly record struct HydrationKey(
        string ProviderType,
        string ContentType,
        string ExternalId,
        string Language)
    {
        internal static HydrationKey Create(
            string providerType,
            string contentType,
            int externalId,
            string language)
        {
            return new(
                Normalize(providerType),
                Normalize(contentType),
                externalId.ToString(CultureInfo.InvariantCulture),
                Normalize(language));
        }
    }
}
