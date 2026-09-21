using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Sdk.Caching;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed class QueryCoordinator : IQueryCoordinator, IDisposable
{
    private sealed class CoalescingStore<TValue>(
        ICacheFactory cacheFactory,
        string name,
        int capacity,
        TimeSpan positiveTtl,
        TimeSpan negativeTtl)
    {
        internal sealed class Lease : IDisposable
        {
            private readonly CoalescingStore<TValue> _cache;
            private readonly CancellationTokenRegistration _registration;
            private int _released;

            internal Lease(CoalescingStore<TValue> cache, string key, TValue value)
            {
                _cache = cache;
                Key = key;
                CompletedAtLookup = true;
                Completion = System.Threading.Tasks.Task.FromResult(value);
            }

            internal Lease(
                CoalescingStore<TValue> cache,
                string key,
                Entry entry,
                bool completedAtLookup,
                CancellationToken cancellationToken)
            {
                _cache = cache;
                Key = key;
                Entry = entry;
                CompletedAtLookup = completedAtLookup;
                Completion = entry.Source.Task;
                if (!completedAtLookup && cancellationToken.CanBeCanceled)
                {
                    _registration = cancellationToken.Register(Release);
                }
            }

            internal string Key { get; }

            internal Entry? Entry { get; }

            internal bool CompletedAtLookup { get; }

            internal Task<TValue> Completion { get; }

            internal CancellationToken FillToken => Entry?.FillCancellation.Token ?? CancellationToken.None;

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

            internal required TaskCompletionSource<TValue> Source { get; init; }

            internal required CancellationTokenSource FillCancellation { get; init; }

            internal int WaiterCount { get; set; }

            internal bool FillStarted { get; set; }

            internal bool TryDisposeFill() => Interlocked.Exchange(ref _fillDisposed, 1) == 0;
        }

        [SuppressMessage("Ordering", "SA1201", Justification = "Nested types come first in this codebase.")]
        private readonly object _gate = new();

        private readonly Dictionary<string, Entry> _entries = [];

        private readonly ICache _positive = cacheFactory.GetOrCreate(name, () => new CachePolicy
        {
            Freshness = positiveTtl,
            Retention = positiveTtl,
            MaximumEntries = capacity
        });

        private readonly ICache _negative = cacheFactory.GetOrCreate(name + "-negative", () => new CachePolicy
        {
            Freshness = negativeTtl,
            Retention = negativeTtl,
            MaximumEntries = capacity
        });

        internal Lease Acquire(
            string key,
            CancellationToken cancellationToken,
            out bool owner,
            out bool completedAtLookup)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entry entry;
            lock (_gate)
            {
                if (_positive.TryGet<TValue>(key, out var cached) || _negative.TryGet(key, out cached))
                {
                    owner = false;
                    completedAtLookup = true;
                    return new Lease(this, key, cached!);
                }

                if (_entries.TryGetValue(key, out var existing))
                {
                    entry = existing;
                    owner = false;
                    completedAtLookup = existing.Source.Task.IsCompleted;
                    if (!completedAtLookup)
                    {
                        entry.WaiterCount++;
                    }
                }
                else
                {
                    entry = new Entry
                    {
                        Source = new TaskCompletionSource<TValue>(
                            TaskCreationOptions.RunContinuationsAsynchronously),
                        FillCancellation = new CancellationTokenSource(),
                        WaiterCount = 1
                    };
                    _entries.Add(key, entry);
                    owner = true;
                    completedAtLookup = false;
                }
            }

            return new Lease(this, key, entry, completedAtLookup, cancellationToken);
        }

        internal bool TryStartFill(Lease lease)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(lease.Key, out var entry) ||
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

        internal void Complete(Lease lease, TValue value, bool negative, bool retain)
        {
            TaskCompletionSource<TValue> source;
            lock (_gate)
            {
                if (!_entries.TryGetValue(lease.Key, out var entry) ||
                    !ReferenceEquals(entry, lease.Entry))
                {
                    return;
                }

                source = entry.Source;
                if (retain)
                {
                    (negative ? _negative : _positive).Set(lease.Key, value);
                }

                _entries.Remove(lease.Key);
            }

            source.TrySetResult(value);
        }

        internal void Fail(Lease lease, Exception exception)
        {
            var source = default(TaskCompletionSource<TValue>);
            lock (_gate)
            {
                if (_entries.TryGetValue(lease.Key, out var entry) &&
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

        internal void DisposeFill(Lease lease)
        {
            if (lease.Entry is { } entry && entry.TryDisposeFill())
            {
                entry.FillCancellation.Dispose();
            }
        }

        private void Release(Lease lease)
        {
            Entry? released = null;
            var disposeCancellation = false;
            lock (_gate)
            {
                if (_entries.TryGetValue(lease.Key, out var entry) &&
                    ReferenceEquals(entry, lease.Entry) &&
                    !lease.CompletedAtLookup)
                {
                    entry.WaiterCount--;
                    if (entry.WaiterCount == 0 && !entry.Source.Task.IsCompleted)
                    {
                        released = entry;
                        disposeCancellation = !entry.FillStarted;
                        _entries.Remove(lease.Key);
                    }
                }
            }

            if (released is not null)
            {
                released.FillCancellation.Cancel();
                released.Source.TrySetCanceled();
                if (disposeCancellation && released.TryDisposeFill())
                {
                    released.FillCancellation.Dispose();
                }
            }
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
        CoalescingStore<EntityCacheValue>.Lease Lease);

    [SuppressMessage("Ordering", "SA1201", Justification = "Nested types come first in this codebase.")]
    private static readonly TimeSpan EntityTtl = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan MissingEntityTtl = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan IdentityTtl = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan NegativeIdentityTtl = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan EntitySearchTtl = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan NegativeEntitySearchTtl = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan HierarchyTtl = TimeSpan.FromMinutes(10);

    private readonly IWikidataClient _client;

    private readonly IServiceScope _cacheScope;

    private readonly CoalescingStore<EntityCacheValue> _entities;

    private readonly CoalescingStore<EntityResolution> _identities;

    private readonly CoalescingStore<IReadOnlyList<Dictionary<string, SparqlBindingDto>>> _searches;

    private readonly CoalescingStore<IReadOnlyList<string>> _entitySearches;

    private readonly CoalescingStore<HierarchyResult> _hierarchies;

    public QueryCoordinator(IWikidataClient client, IServiceScopeFactory scopeFactory)
    {
        _client = client;
        _cacheScope = scopeFactory.CreateScope();
        var cacheFactory = _cacheScope.ServiceProvider.GetRequiredService<ICacheFactory>();
        _entities = new CoalescingStore<EntityCacheValue>(
            cacheFactory, "wikidata-entities", 1024, EntityTtl, MissingEntityTtl);
        _identities = new CoalescingStore<EntityResolution>(
            cacheFactory, "wikidata-identities", 512, IdentityTtl, NegativeIdentityTtl);
        _searches = new CoalescingStore<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>(
            cacheFactory, "wikidata-searches", 256, SearchTtl, SearchTtl);
        _entitySearches = new CoalescingStore<IReadOnlyList<string>>(
            cacheFactory, "wikidata-entitysearches", 256, EntitySearchTtl, NegativeEntitySearchTtl);
        _hierarchies = new CoalescingStore<HierarchyResult>(
            cacheFactory, "wikidata-hierarchies", 128, HierarchyTtl, HierarchyTtl);
    }

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
        var leases = new Dictionary<string, CoalescingStore<EntityCacheValue>.Lease>(
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
                var value = await AwaitAsync(pair.Value.Completion, cancellationToken).ConfigureAwait(false);
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
            value => value.Kind != EntityResolutionKind.Resolved,
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
                _ => false,
                _ => true);
        }

        var rows = await AwaitAsync(lease.Completion, cancellationToken).ConfigureAwait(false);
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
            value => value.Count == 0,
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
            _ => false,
            _ => true,
            cancellationToken);

    public void Dispose()
    {
        _cacheScope.Dispose();
    }

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
        CoalescingStore<TValue> cache,
        string key,
        Func<CancellationToken, Task<TValue>> factory,
        Func<TValue, bool> negative,
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
                negative,
                retain);
        }

        return await AwaitAsync(lease.Completion, cancellationToken).ConfigureAwait(false);
    }

    private static async Task FillAsync<TValue>(
        CoalescingStore<TValue> cache,
        CoalescingStore<TValue>.Lease lease,
        Func<CancellationToken, Task<TValue>> factory,
        Func<TValue, bool> negative,
        Func<TValue, bool> retain)
    {
        try
        {
            var value = await factory(lease.FillToken).ConfigureAwait(false);
            cache.Complete(lease, value, negative(value), retain(value));
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
                var entities = await _client.GetEntitiesAsync(
                    [.. batch.Select(owner => owner.Qid)],
                    context,
                    lifetime.Token).ConfigureAwait(false);
                foreach (var owner in batch)
                {
                    entities.TryGetValue(owner.Qid, out var entity);
                    _entities.Complete(
                        owner.Lease,
                        new EntityCacheValue { Entity = entity },
                        entity is null,
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
}
