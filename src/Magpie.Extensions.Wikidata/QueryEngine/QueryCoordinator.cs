using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata.Mapping;

using Microsoft.Extensions.Caching.Memory;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed class QueryCoordinator(IWikidataClient client) : IQueryCoordinator
{
    private static readonly TimeSpan EntityTtl = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan MissingEntityTtl = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan IdentityTtl = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan NegativeIdentityTtl = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan EntitySearchTtl = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan NegativeEntitySearchTtl = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan HierarchyTtl = TimeSpan.FromMinutes(10);

    private readonly CoalescingStore<string, EntityCacheValue> _entities = new(1024);
    private readonly CoalescingStore<string, EntityResolution> _identities = new(512);

    private readonly CoalescingStore<string, IReadOnlyList<Dictionary<string, SparqlBindingDto>>> _searches = new(256);

    private readonly CoalescingStore<string, IReadOnlyList<string>> _entitySearches = new(256);
    private readonly CoalescingStore<string, HierarchyResult> _hierarchies = new(128);

    public async Task<EntityDto?> GetEntityAsync(
        string qid,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var entities = await GetEntitiesAsync([qid], context, cancellationToken).ConfigureAwait(false);
        return entities.TryGetValue(qid, out var entity) ? entity : null;
    }

    public async Task<IReadOnlyDictionary<string, EntityDto>> GetEntitiesAsync(
        IReadOnlyCollection<string> qids,
        RequestContext context,
        CancellationToken cancellationToken,
        Action<SearchCacheProvenance>? provenanceObserver = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var leases = new Dictionary<string, CoalescingStore<string, EntityCacheValue>.Lease>(
            StringComparer.OrdinalIgnoreCase);
        var owners = new List<EntityOwner>();
        var languageKey = EntityLanguageKey(context);
        try
        {
            foreach (var qid in qids.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var key = languageKey + "|" + qid.ToUpperInvariant();
                var lease = _entities.Acquire(
                    key,
                    cancellationToken,
                    out var owner,
                    out var completedAtLookup);
                provenanceObserver?.Invoke(Provenance(owner, completedAtLookup));
                leases[qid] = lease;
                if (owner)
                {
                    owners.Add(new EntityOwner(qid, lease));
                }
            }

            if (owners.Count > 0)
            {
                _ = FillEntitiesAsync(owners, context);
            }

            var results = new Dictionary<string, EntityDto>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in leases)
            {
                var value = await AwaitAsync(pair.Value.Task, cancellationToken).ConfigureAwait(false);
                if (value.Entity is not null)
                {
                    results[pair.Key] = value.Entity;
                }
            }

            return results;
        }
        finally
        {
            foreach (var lease in leases.Values)
            {
                lease.Dispose();
            }
        }
    }

    public Task<EntityResolution> GetIdentityAsync(
        string key,
        Func<CancellationToken, Task<EntityResolution>> factory,
        CancellationToken cancellationToken) => GetOrCreateAsync(
            _identities,
            key,
            factory,
            value => value.Kind == EntityResolutionKind.Resolved ? IdentityTtl : NegativeIdentityTtl,
            value => value.Kind != EntityResolutionKind.Ambiguous,
            cancellationToken);

    public async Task<SearchCacheResult> GetSearchAsync(
        string key,
        Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>> factory,
        CancellationToken cancellationToken,
        Action<SearchCacheProvenance>? provenanceObserver = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = _searches.Acquire(
            key,
            cancellationToken,
            out var owner,
            out var completedAtLookup);
        var provenance = Provenance(owner, completedAtLookup);
        provenanceObserver?.Invoke(provenance);
        if (owner && _searches.TryStartFill(lease))
        {
            _ = FillAsync(
                _searches,
                lease,
                factory,
                _ => SearchTtl,
                _ => true);
        }

        var rows = await AwaitAsync(lease.Task, cancellationToken).ConfigureAwait(false);
        return new SearchCacheResult(rows, provenance);
    }

    public Task<IReadOnlyList<string>> GetEntitySearchAsync(
        string key,
        Func<CancellationToken, Task<IReadOnlyList<string>>> factory,
        CancellationToken cancellationToken,
        Action<SearchCacheProvenance>? provenanceObserver) => GetOrCreateAsync(
            _entitySearches,
            key,
            factory,
            value => value.Count == 0 ? NegativeEntitySearchTtl : EntitySearchTtl,
            _ => true,
            cancellationToken,
            provenanceObserver);

    public Task<HierarchyResult> GetHierarchyAsync(
        string key,
        Func<CancellationToken, Task<HierarchyResult>> factory,
        CancellationToken cancellationToken) => GetOrCreateAsync(
            _hierarchies,
            key,
            factory,
            _ => HierarchyTtl,
            _ => true,
            cancellationToken);

    private static string EntityLanguageKey(RequestContext context)
    {
        if (context.FetchAllLanguages)
        {
            return "*";
        }

        var languages = from value in context.FallbackLanguages
                        select value.Trim().ToLowerInvariant();
        return string.Join(
            ",",
            languages.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal));
    }

    private static SearchCacheProvenance Provenance(bool owner, bool completedAtLookup) => owner
        ? SearchCacheProvenance.Remote
        : completedAtLookup
            ? SearchCacheProvenance.Cache
            : SearchCacheProvenance.InFlight;

    private static async Task<TValue> GetOrCreateAsync<TValue>(
        CoalescingStore<string, TValue> cache,
        string key,
        Func<CancellationToken, Task<TValue>> factory,
        Func<TValue, TimeSpan> ttl,
        Func<TValue, bool> retain,
        CancellationToken cancellationToken,
        Action<SearchCacheProvenance>? provenanceObserver = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = cache.Acquire(
            key,
            cancellationToken,
            out var owner,
            out var completedAtLookup);
        provenanceObserver?.Invoke(Provenance(owner, completedAtLookup));
        if (owner && cache.TryStartFill(lease))
        {
            _ = FillAsync(
                cache,
                lease,
                factory,
                ttl,
                retain);
        }

        return await AwaitAsync(lease.Task, cancellationToken).ConfigureAwait(false);
    }

    private static async Task FillAsync<TValue>(
        CoalescingStore<string, TValue> cache,
        CoalescingStore<string, TValue>.Lease lease,
        Func<CancellationToken, Task<TValue>> factory,
        Func<TValue, TimeSpan> ttl,
        Func<TValue, bool> retain)
    {
        try
        {
            var value = await factory(lease.FillToken).ConfigureAwait(false);
            cache.Complete(lease, value, ttl(value), retain(value));
        }
        catch (Exception exception)
        {
            cache.Fail(lease, exception);
        }
        finally
        {
            cache.DisposeFill(lease);
        }
    }

    private static async Task<TValue> AwaitAsync<TValue>(Task<TValue> task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!cancellationToken.CanBeCanceled)
        {
            return await task.ConfigureAwait(false);
        }

        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => cancelled.TrySetResult(true));
        await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return await task.ConfigureAwait(false);
    }

    private async Task FillEntitiesAsync(
        IReadOnlyList<EntityOwner> owners,
        RequestContext context)
    {
        for (var offset = 0; offset < owners.Count; offset += 50)
        {
            var batch = owners.Skip(offset)
                .Take(50)
                .Where(owner => _entities.TryStartFill(owner.Lease))
                .ToArray();
            if (batch.Length == 0)
            {
                continue;
            }

            using var lifetime = new CombinedFillLifetime(batch.Select(owner => owner.Lease.FillToken));
            try
            {
                var entities = await client.GetEntitiesAsync(
                    [.. batch.Select(owner => owner.Qid)],
                    context,
                    lifetime.Token).ConfigureAwait(false);
                foreach (var owner in batch)
                {
                    entities.TryGetValue(owner.Qid, out var entity);
                    _entities.Complete(
                        owner.Lease,
                        new EntityCacheValue { Entity = entity },
                        entity is null ? MissingEntityTtl : EntityTtl,
                        retain: true);
                }
            }
            catch (Exception exception)
            {
                foreach (var owner in batch)
                {
                    _entities.Fail(owner.Lease, exception);
                }
            }
            finally
            {
                foreach (var owner in batch)
                {
                    _entities.DisposeFill(owner.Lease);
                }
            }
        }
    }

    private sealed class CoalescingStore<TKey, TValue>(int capacity)
        where TKey : notnull
    {
        private readonly object _gate = new();
        private readonly MemoryCache _entries = new(new MemoryCacheOptions { SizeLimit = capacity });

        public Lease Acquire(
            TKey key,
            CancellationToken cancellationToken,
            out bool owner,
            out bool completedAtLookup)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entry entry;
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out Entry? existing) && existing is not null)
                {
                    entry = existing;
                    owner = false;
                    completedAtLookup = existing.Source.Task.IsCompleted;
                    if (!completedAtLookup)
                    {
                        entry.WaiterCount++;
                    }

                    return new Lease(this, key, entry, completedAtLookup, cancellationToken);
                }

                var source = new TaskCompletionSource<TValue>(TaskCreationOptions.RunContinuationsAsynchronously);
                entry = new Entry
                {
                    Source = source,
                    FillCancellation = new CancellationTokenSource(),
                    WaiterCount = 1
                };
                Store(key, entry, ttl: null);
                owner = true;
                completedAtLookup = false;
            }

            return new Lease(this, key, entry, completedAtLookup: false, cancellationToken);
        }

        public bool TryStartFill(Lease lease)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(lease.Key, out Entry? entry) ||
                    !ReferenceEquals(entry, lease.Entry) ||
                    entry.Source.Task.IsCompleted ||
                    entry.WaiterCount == 0 ||
                    entry.FillStarted)
                {
                    return false;
                }

                entry.FillStarted = true;
                return true;
            }
        }

        public void Complete(Lease lease, TValue value, TimeSpan ttl, bool retain)
        {
            TaskCompletionSource<TValue>? source = null;
            lock (_gate)
            {
                if (!_entries.TryGetValue(lease.Key, out Entry? entry) ||
                    !ReferenceEquals(entry, lease.Entry))
                {
                    return;
                }

                source = entry.Source;
                if (retain)
                {
                    Store(lease.Key, entry, ttl);
                }
                else
                {
                    _entries.Remove(lease.Key);
                }
            }

            source.TrySetResult(value);
        }

        public void Fail(Lease lease, Exception exception)
        {
            TaskCompletionSource<TValue>? source = null;
            lock (_gate)
            {
                if (_entries.TryGetValue(lease.Key, out Entry? entry) &&
                    ReferenceEquals(entry, lease.Entry) &&
                    !entry.Source.Task.IsCompleted)
                {
                    source = entry.Source;
                    _entries.Remove(lease.Key);
                }
            }

            if (exception is OperationCanceledException)
            {
                source?.TrySetCanceled();
            }
            else
            {
                source?.TrySetException(exception);
            }
        }

        public void DisposeFill(Lease lease)
        {
            if (lease.Entry.TryDisposeFill())
            {
                lease.Entry.FillCancellation.Dispose();
            }
        }

        private void Release(Lease lease)
        {
            CancellationTokenSource? cancellation = null;
            TaskCompletionSource<TValue>? source = null;
            var disposeCancellation = false;
            lock (_gate)
            {
                if (_entries.TryGetValue(lease.Key, out Entry? entry) &&
                    ReferenceEquals(entry, lease.Entry) &&
                    !lease.CompletedAtLookup)
                {
                    entry.WaiterCount--;
                    if (entry.WaiterCount == 0 && !entry.Source.Task.IsCompleted)
                    {
                        cancellation = entry.FillCancellation;
                        source = entry.Source;
                        disposeCancellation = !entry.FillStarted;
                        _entries.Remove(lease.Key);
                    }
                }
            }

            if (cancellation is not null)
            {
                cancellation.Cancel();
                source?.TrySetCanceled();
                if (disposeCancellation && lease.Entry.TryDisposeFill())
                {
                    cancellation.Dispose();
                }
            }
        }

        private void Store(TKey key, Entry entry, TimeSpan? ttl)
        {
            using var item = _entries.CreateEntry(key);
            item.Value = entry;
            item.Size = 1;
            if (ttl is { } window)
            {
                item.AbsoluteExpirationRelativeToNow = window;
            }
            else
            {
                item.Priority = CacheItemPriority.NeverRemove;
            }
        }

        internal sealed class Lease : IDisposable
        {
            private readonly CoalescingStore<TKey, TValue> _cache;
            private readonly CancellationTokenRegistration _registration;
            private int _released;

            public Lease(
                CoalescingStore<TKey, TValue> cache,
                TKey key,
                Entry entry,
                bool completedAtLookup,
                CancellationToken cancellationToken)
            {
                _cache = cache;
                Key = key;
                Entry = entry;
                CompletedAtLookup = completedAtLookup;
                if (!completedAtLookup && cancellationToken.CanBeCanceled)
                {
                    _registration = cancellationToken.Register(Release);
                }
            }

            public TKey Key { get; }

            public Entry Entry { get; }

            public bool CompletedAtLookup { get; }

            public Task<TValue> Task => Entry.Source.Task;

            public CancellationToken FillToken => Entry.FillCancellation.Token;

            public void Dispose()
            {
                _registration.Dispose();
                Release();
            }

            private void Release()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0)
                {
                    _cache.Release(this);
                }
            }
        }

        internal sealed class Entry
        {
            private int _fillDisposed;

            public required TaskCompletionSource<TValue> Source { get; init; }

            public required CancellationTokenSource FillCancellation { get; init; }

            public int WaiterCount { get; set; }

            public bool FillStarted { get; set; }

            internal bool TryDisposeFill() => Interlocked.Exchange(ref _fillDisposed, 1) == 0;
        }
    }

    private sealed class CombinedFillLifetime : IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        private readonly CancellationTokenRegistration[] _registrations;
        private int _remaining;

        public CombinedFillLifetime(IEnumerable<CancellationToken> tokens)
        {
            var values = tokens.ToArray();
            _remaining = values.Length;
            _registrations = new CancellationTokenRegistration[values.Length];
            for (var index = 0; index < values.Length; index++)
            {
                _registrations[index] = values[index].Register(Release);
            }

            if (values.Length == 0)
            {
                _source.Cancel();
            }
        }

        public CancellationToken Token => _source.Token;

        public void Dispose()
        {
            foreach (var registration in _registrations)
            {
                registration.Dispose();
            }

            _source.Dispose();
        }

        private void Release()
        {
            if (Interlocked.Decrement(ref _remaining) == 0)
            {
                _source.Cancel();
            }
        }
    }

    private sealed record EntityOwner(
        string Qid,
        CoalescingStore<string, EntityCacheValue>.Lease Lease);
}
