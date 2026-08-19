using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Core.Observability;
using Magpie.Sdk.Caching;

namespace Magpie.Core.Caching;

internal sealed class MemoryCacheStore(TimeProvider timeProvider) : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<Cache>> _namedCaches = new(StringComparer.Ordinal);

    internal ICache GetOrCreate(string name, Func<CachePolicy>? createPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _namedCaches.GetOrAdd(
            name,
            _ => new Lazy<Cache>(
                () => new Cache(name, "shared", createPolicy?.Invoke() ?? new CachePolicy(), timeProvider),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    internal Cache CreateScoped(string name, CachePolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Cache(name, "private", policy, timeProvider);
    }

    public void Dispose()
    {
        foreach (var cache in _namedCaches.Values)
        {
            if (cache.IsValueCreated)
            {
                cache.Value.Dispose();
            }
        }

        _namedCaches.Clear();
    }

    internal sealed class Cache : ICache, IDisposable
    {
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, PessimisticPopulation> _populations = new(StringComparer.Ordinal);
        private readonly LinkedList<string> _recency = [];
        private readonly Lock _lock = new();
        private readonly string _lifetime;
        private readonly string _name;
        private readonly CachePolicy _policy;
        private readonly TimeProvider _timeProvider;
        private bool _disposed;

        internal Cache(string name, string lifetime, CachePolicy policy, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(policy);
            if (policy.MaximumLevel < CacheLevel.Memory ||
                !ValidLifetime(policy.Freshness) ||
                !ValidLifetime(policy.Retention) ||
                policy.MaximumEntries <= 0 ||
                !Enum.IsDefined(policy.EvictionBehavior) ||
                !Enum.IsDefined(policy.ConcurrencyMode))
            {
                throw new ArgumentOutOfRangeException(nameof(policy));
            }

            _name = name;
            _lifetime = lifetime;
            _policy = policy;
            _timeProvider = timeProvider;
            EntryLifetime = MinimumLifetime(policy.Freshness, policy.Retention);
        }

        private TimeSpan? EntryLifetime { get; }

        private static TimeSpan? MinimumLifetime(TimeSpan freshness, TimeSpan retention)
        {
            if (freshness == Timeout.InfiniteTimeSpan)
            {
                return retention == Timeout.InfiniteTimeSpan ? null : retention;
            }

            return retention == Timeout.InfiniteTimeSpan || freshness <= retention ? freshness : retention;
        }

        private static bool ValidLifetime(TimeSpan lifetime)
        {
            return lifetime > TimeSpan.Zero || lifetime == Timeout.InfiniteTimeSpan;
        }

        public bool TryGet<T>(string key, out T? value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            var hit = TryGetCore(key, out value);
            RecordOutcome(hit);
            return hit;
        }

        public async ValueTask<T> GetOrAddAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T>> valueFactory,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(valueFactory);
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGet<T>(key, out var cached))
            {
                return cached!;
            }

            if (_policy.ConcurrencyMode == CacheConcurrencyMode.Optimistic)
            {
                var created = await valueFactory(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                Set(key, created);
                return created;
            }

            while (true)
            {
                if (TryGetCore<T>(key, out var cachedAfterMiss))
                {
                    return cachedAfterMiss!;
                }

                PessimisticPopulation? candidate = null;
                var population = _populations.GetOrAdd(
                    key,
                    _ => candidate = new PessimisticPopulation(
                        async sharedCancellationToken =>
                        {
                            try
                            {
                                if (TryGetCore<T>(key, out var cachedBeforeFactory))
                                {
                                    return cachedBeforeFactory!;
                                }

                                var created = await valueFactory(sharedCancellationToken).ConfigureAwait(false);
                                sharedCancellationToken.ThrowIfCancellationRequested();
                                Set(key, created);
                                return created;
                            }
                            finally
                            {
                                ((ICollection<KeyValuePair<string, PessimisticPopulation>>)_populations).Remove(
                                    new KeyValuePair<string, PessimisticPopulation>(key, candidate!));
                            }
                        },
                        populationToRemove =>
                            ((ICollection<KeyValuePair<string, PessimisticPopulation>>)_populations).Remove(
                                new KeyValuePair<string, PessimisticPopulation>(key, populationToRemove))));
                var joined = ReferenceEquals(population, candidate) || population.TryJoin();
                if (!joined)
                {
                    continue;
                }

                try
                {
                    return (T)(await population.Task.WaitAsync(cancellationToken).ConfigureAwait(false))!;
                }
                finally
                {
                    population.Leave();
                }
            }
        }

        public bool Remove(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_entries.TryGetValue(key, out var entry))
                {
                    return false;
                }

                RemoveEntry(entry);
                return true;
            }
        }

        public void Set<T>(string key, T value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var now = _timeProvider.GetUtcNow();
                foreach (var expired in _entries.Values
                             .Where(entry => entry.ExpiresAt is { } expiresAt && expiresAt <= now)
                             .ToArray())
                {
                    RemoveEntry(expired);
                }

                if (_entries.TryGetValue(key, out var existing))
                {
                    RemoveEntry(existing);
                }

                var node = _recency.AddFirst(key);
                _entries.Add(key, new Entry(value, EntryLifetime is { } lifetime ? now + lifetime : null, node));
                while (_entries.Count > _policy.MaximumEntries)
                {
                    RemoveEntry(_entries[_recency.Last!.Value]);
                }
            }
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
                _entries.Clear();
                _recency.Clear();
            }
        }

        private void RecordOutcome(bool hit)
        {
            Activity.Current?.SetTag("magpie.cache.outcome", hit ? "hit" : "miss");
            Activity.Current?.SetTag("magpie.cache.hit", hit);
            Activity.Current?.SetTag("magpie.cache.name", _name);
            Activity.Current?.SetTag("magpie.cache.lifetime", _lifetime);
            Activity.Current?.SetTag("magpie.cache.backend", "memory");
            Activity.Current?.SetTag("magpie.cache.level", "memory");
            (hit ? DataTelemetry.CacheHit : DataTelemetry.CacheMiss).Add(
                1,
                new KeyValuePair<string, object?>("cache", _name),
                new KeyValuePair<string, object?>("lifetime", _lifetime),
                new KeyValuePair<string, object?>("backend", "memory"),
                new KeyValuePair<string, object?>("level", "memory"));
        }

        private bool TryGetCore<T>(string key, out T? value)
        {
            value = default;
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_entries.TryGetValue(key, out var entry))
                {
                    return false;
                }

                if (entry.ExpiresAt is { } expiresAt && expiresAt <= _timeProvider.GetUtcNow())
                {
                    RemoveEntry(entry);
                    return false;
                }

                _recency.Remove(entry.Node);
                _recency.AddFirst(entry.Node);
                value = (T?)entry.Value;
                return true;
            }
        }

        private void RemoveEntry(Entry entry)
        {
            _entries.Remove(entry.Node.Value);
            _recency.Remove(entry.Node);
        }

        private sealed record Entry(object? Value, DateTimeOffset? ExpiresAt, LinkedListNode<string> Node);

        private sealed class PessimisticPopulation
        {
            private readonly CancellationTokenSource _cancellation = new();
            private readonly Action<PessimisticPopulation> _detach;
            private readonly Lock _lock = new();
            private readonly Lazy<Task<object?>> _task;
            private bool _accepting = true;
            private int _waiters = 1;

            internal PessimisticPopulation(
                Func<CancellationToken, Task<object?>> factory,
                Action<PessimisticPopulation> detach)
            {
                _detach = detach;
                _task = new Lazy<Task<object?>>(
                    () => factory(_cancellation.Token),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }

            internal Task<object?> Task => _task.Value;

            internal bool TryJoin()
            {
                lock (_lock)
                {
                    if (!_accepting)
                    {
                        return false;
                    }

                    _waiters++;
                    return true;
                }
            }

            internal void Leave()
            {
                var cancel = false;
                lock (_lock)
                {
                    _waiters--;
                    if (_waiters == 0 && !Task.IsCompleted)
                    {
                        _accepting = false;
                        cancel = true;
                    }
                }

                if (cancel)
                {
                    _detach(this);
                    _cancellation.Cancel();
                }
            }
        }
    }
}
