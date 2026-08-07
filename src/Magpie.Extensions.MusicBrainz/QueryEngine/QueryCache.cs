using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz.Mapping;

using Microsoft.Extensions.Caching.Memory;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed class QueryCache : IQueryCache
{
    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EntityTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MissingEntityTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CoverTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan MissingCoverTtl = TimeSpan.FromMinutes(30);

    private readonly object _loadGate = new();
    private readonly MemoryCache _searchPages = Create(256);
    private readonly MemoryCache _verifiedScans = Create(128);
    private readonly MemoryCache _entities = Create(1024);
    private readonly MemoryCache _missingEntities = Create(256);
    private readonly MemoryCache _browsePages = Create(256);
    private readonly MemoryCache _artistBrowsePages = Create(256);
    private readonly MemoryCache _artistResolutions = Create(256);
    private readonly MemoryCache _isrcResults = Create(256);
    private readonly MemoryCache _covers = Create(512);
    private readonly MemoryCache _missingCovers = Create(256);

    private readonly Dictionary<SearchCacheKey, Task<FetchResult<SearchPage>>> _searchLoads = [];
    private readonly Dictionary<string, Task<FetchResult<VerifiedScan>>> _scanLoads = [];
    private readonly Dictionary<EntityCacheKey, Task<FetchResult<EntityResponseDto>>> _entityLoads = [];

    private readonly Dictionary<BrowseCacheKey, Task<FetchResult<ReleaseGroupSearchResponseDto>>>
        _browseLoads = [];

    private readonly Dictionary<ArtistBrowseCacheKey, Task<FetchResult<ArtistBrowseResponseDto>>>
        _artistBrowseLoads = [];

    private readonly Dictionary<ArtistResolutionCacheKey, Task<FetchResult<ArtistResolutionResponse>>>
        _artistResolutionLoads = [];

    private readonly Dictionary<string, Task<FetchResult<SearchPage>>> _isrcLoads = [];
    private readonly Dictionary<CoverCacheKey, Task<FetchResult<Uri>>> _coverLoads = [];

    public Task<FetchResult<SearchPage>> GetSearchPageAsync(
        SearchCacheKey key,
        Func<CancellationToken, Task<FetchResult<SearchPage>>> loader,
        CancellationToken cancellationToken)
    {
        return GetCachedAsync(key, _searchPages, SearchTtl, _searchLoads, loader, cancellationToken);
    }

    public async Task<FetchResult<VerifiedScan>> GetVerifiedScanAsync(
        string fingerprint,
        Func<VerifiedScan, bool> isSufficient,
        Func<VerifiedScan?, CancellationToken, Task<FetchResult<VerifiedScan>>> loader,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            _verifiedScans.TryGetValue(fingerprint, out VerifiedScan? existing);
            if (existing is not null && isSufficient(existing))
            {
                return Success(existing);
            }

            Task<FetchResult<VerifiedScan>> shared;
            lock (_loadGate)
            {
                if (!_scanLoads.TryGetValue(fingerprint, out shared!))
                {
                    shared = LoadScanAsync(fingerprint, existing, loader);
                    _scanLoads[fingerprint] = shared;
                }
            }

            var result = await AwaitAsync(shared, cancellationToken).ConfigureAwait(false);
            if (result.Outcome != FetchOutcome.Success || result.Value is null || isSufficient(result.Value))
            {
                return result;
            }
        }
    }

    public async Task<FetchResult<EntityResponseDto>> GetEntityAsync(
        EntityCacheKey key,
        Func<CancellationToken, Task<FetchResult<EntityResponseDto>>> loader,
        CancellationToken cancellationToken)
    {
        var normalizedKey = key.Normalize();
        foreach (var cachedKey in _entities.Keys)
        {
            if (cachedKey is not EntityCacheKey candidate || !candidate.Satisfies(normalizedKey))
            {
                continue;
            }

            if (_entities.TryGetValue(candidate, out EntityResponseDto? cached) && cached is not null)
            {
                return Success(cached);
            }
        }

        var identity = new EntityIdentityKey { Kind = normalizedKey.Kind, Mbid = normalizedKey.Mbid };
        if (_missingEntities.TryGetValue(identity, out _))
        {
            return NotFound<EntityResponseDto>();
        }

        Task<FetchResult<EntityResponseDto>> shared = null!;
        lock (_loadGate)
        {
            shared = _entityLoads.FirstOrDefault(load => load.Key.Satisfies(normalizedKey)).Value;

            if (shared is null)
            {
                shared = LoadEntityAsync(normalizedKey, identity, loader);
                _entityLoads[normalizedKey] = shared;
            }
        }

        return await AwaitAsync(shared, cancellationToken).ConfigureAwait(false);
    }

    public Task<FetchResult<ReleaseGroupSearchResponseDto>> GetBrowsePageAsync(
        BrowseCacheKey key,
        Func<CancellationToken, Task<FetchResult<ReleaseGroupSearchResponseDto>>> loader,
        CancellationToken cancellationToken)
    {
        return GetCachedAsync(key, _browsePages, EntityTtl, _browseLoads, loader, cancellationToken);
    }

    public Task<FetchResult<ArtistBrowseResponseDto>> GetArtistBrowsePageAsync(
        ArtistBrowseCacheKey key,
        Func<CancellationToken, Task<FetchResult<ArtistBrowseResponseDto>>> loader,
        CancellationToken cancellationToken)
    {
        return GetCachedAsync(key, _artistBrowsePages, EntityTtl, _artistBrowseLoads, loader, cancellationToken);
    }

    public Task<FetchResult<ArtistResolutionResponse>> GetArtistResolutionAsync(
        ArtistResolutionCacheKey key,
        Func<CancellationToken, Task<FetchResult<ArtistResolutionResponse>>> loader,
        CancellationToken cancellationToken)
    {
        return GetCachedAsync(key, _artistResolutions, SearchTtl, _artistResolutionLoads, loader, cancellationToken);
    }

    public async Task<FetchResult<SearchPage>> GetIsrcAsync(
        string isrc,
        Func<CancellationToken, Task<FetchResult<SearchPage>>> loader,
        CancellationToken cancellationToken)
    {
        if (_isrcResults.TryGetValue(isrc, out SearchPage? cached) && cached is not null)
        {
            return Success(cached);
        }

        var identity = new EntityIdentityKey { Kind = EntityKind.Recording, Mbid = isrc };
        if (_missingEntities.TryGetValue(identity, out _))
        {
            return NotFound<SearchPage>();
        }

        Task<FetchResult<SearchPage>> shared;
        lock (_loadGate)
        {
            if (!_isrcLoads.TryGetValue(isrc, out shared!))
            {
                shared = LoadIsrcAsync(isrc, identity, loader);
                _isrcLoads[isrc] = shared;
            }
        }

        return await AwaitAsync(shared, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FetchResult<Uri>> GetCoverAsync(
        CoverCacheKey key,
        Func<CancellationToken, Task<FetchResult<Uri>>> loader,
        CancellationToken cancellationToken)
    {
        if (_covers.TryGetValue(key, out Uri? cover) && cover is not null)
        {
            return Success(cover);
        }

        if (_missingCovers.TryGetValue(key, out _))
        {
            return NotFound<Uri>();
        }

        Task<FetchResult<Uri>> shared;
        lock (_loadGate)
        {
            if (!_coverLoads.TryGetValue(key, out shared!))
            {
                shared = LoadCoverAsync(key, loader);
                _coverLoads[key] = shared;
            }
        }

        return await AwaitAsync(shared, cancellationToken).ConfigureAwait(false);
    }

    private static MemoryCache Create(int capacity)
    {
        return new MemoryCache(new MemoryCacheOptions { SizeLimit = capacity });
    }

    private static MemoryCacheEntryOptions Entry(TimeSpan ttl)
    {
        return new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = ttl };
    }

    private static async Task<T> AwaitAsync<T>(Task<T> task, CancellationToken cancellationToken)
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

    private static FetchResult<T> Success<T>(T value)
        where T : class
    {
        return new() { Outcome = FetchOutcome.Success, Value = value };
    }

    private static FetchResult<T> NotFound<T>()
        where T : class
    {
        return new() { Outcome = FetchOutcome.NotFound };
    }

    private async Task<FetchResult<TValue>> GetCachedAsync<TKey, TValue>(
        TKey key,
        MemoryCache cache,
        TimeSpan ttl,
        Dictionary<TKey, Task<FetchResult<TValue>>> loads,
        Func<CancellationToken, Task<FetchResult<TValue>>> loader,
        CancellationToken cancellationToken)
        where TKey : notnull
        where TValue : class
    {
        if (cache.TryGetValue(key, out TValue? cached) && cached is not null)
        {
            return Success(cached);
        }

        Task<FetchResult<TValue>> shared;
        lock (_loadGate)
        {
            if (!loads.TryGetValue(key, out shared!))
            {
                shared = LoadCachedAsync(key, cache, ttl, loads, loader);
                loads[key] = shared;
            }
        }

        return await AwaitAsync(shared, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FetchResult<TValue>> LoadCachedAsync<TKey, TValue>(
        TKey key,
        MemoryCache cache,
        TimeSpan ttl,
        Dictionary<TKey, Task<FetchResult<TValue>>> loads,
        Func<CancellationToken, Task<FetchResult<TValue>>> loader)
        where TKey : notnull
        where TValue : class
    {
        await Task.Yield();
        try
        {
            var result = await loader(default).ConfigureAwait(false);
            if (result is { Outcome: FetchOutcome.Success, Value: { } value })
            {
                cache.Set(key, value, Entry(ttl));
            }

            return result;
        }
        finally
        {
            lock (_loadGate)
            {
                loads.Remove(key);
            }
        }
    }

    private async Task<FetchResult<VerifiedScan>> LoadScanAsync(
        string fingerprint,
        VerifiedScan? existing,
        Func<VerifiedScan?, CancellationToken, Task<FetchResult<VerifiedScan>>> loader)
    {
        await Task.Yield();
        try
        {
            var result = await loader(existing, default).ConfigureAwait(false);
            if (result is { Outcome: FetchOutcome.Success, Value: { } value })
            {
                _verifiedScans.Set(fingerprint, value, Entry(SearchTtl));
            }

            return result;
        }
        finally
        {
            lock (_loadGate)
            {
                _scanLoads.Remove(fingerprint);
            }
        }
    }

    private async Task<FetchResult<EntityResponseDto>> LoadEntityAsync(
        EntityCacheKey key,
        EntityIdentityKey identity,
        Func<CancellationToken, Task<FetchResult<EntityResponseDto>>> loader)
    {
        await Task.Yield();
        try
        {
            var result = await loader(default).ConfigureAwait(false);
            if (result is { Outcome: FetchOutcome.Success, Value: { } value })
            {
                _entities.Set(key, value, Entry(EntityTtl));
            }
            else if (result.Outcome == FetchOutcome.NotFound)
            {
                _missingEntities.Set(identity, true, Entry(MissingEntityTtl));
            }

            return result;
        }
        finally
        {
            lock (_loadGate)
            {
                _entityLoads.Remove(key);
            }
        }
    }

    private async Task<FetchResult<SearchPage>> LoadIsrcAsync(
        string isrc,
        EntityIdentityKey identity,
        Func<CancellationToken, Task<FetchResult<SearchPage>>> loader)
    {
        await Task.Yield();
        try
        {
            var result = await loader(default).ConfigureAwait(false);
            if (result is { Outcome: FetchOutcome.Success, Value: { } value })
            {
                _isrcResults.Set(isrc, value, Entry(SearchTtl));
            }
            else if (result.Outcome == FetchOutcome.NotFound)
            {
                _missingEntities.Set(identity, true, Entry(MissingEntityTtl));
            }

            return result;
        }
        finally
        {
            lock (_loadGate)
            {
                _isrcLoads.Remove(isrc);
            }
        }
    }

    private async Task<FetchResult<Uri>> LoadCoverAsync(
        CoverCacheKey key,
        Func<CancellationToken, Task<FetchResult<Uri>>> loader)
    {
        await Task.Yield();
        try
        {
            var result = await loader(default).ConfigureAwait(false);
            if (result is { Outcome: FetchOutcome.Success, Value: { } value })
            {
                _covers.Set(key, value, Entry(CoverTtl));
            }
            else if (result.Outcome == FetchOutcome.NotFound)
            {
                _missingCovers.Set(key, true, Entry(MissingCoverTtl));
            }

            return result;
        }
        finally
        {
            lock (_loadGate)
            {
                _coverLoads.Remove(key);
            }
        }
    }
}
