using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz.Mapping;
using Magpie.Sdk.Caching;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed class QueryCoordinator : IQueryCoordinator, IDisposable
{
    private const int EntityCapacity = 1024;

    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EntityTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MissingEntityTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CoverTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan MissingCoverTtl = TimeSpan.FromMinutes(30);

    private readonly object _loadGate = new();

    private readonly IServiceScope _cacheScope;

    private readonly ICache _searchPages;

    private readonly ICache _verifiedScans;

    private readonly ICache _entities;

    private readonly ICache _missingEntities;

    private readonly ICache _browsePages;

    private readonly ICache _artistBrowsePages;

    private readonly ICache _artistResolutions;

    private readonly ICache _isrcResults;

    private readonly ICache _covers;

    private readonly ICache _missingCovers;

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

    public QueryCoordinator(IServiceScopeFactory scopeFactory)
    {
        _cacheScope = scopeFactory.CreateScope();
        var cacheFactory = _cacheScope.ServiceProvider.GetRequiredService<ICacheFactory>();
        _searchPages = cacheFactory.GetOrCreate("musicbrainz-search-pages", () => Policy(SearchTtl, 256));
        _verifiedScans = cacheFactory.GetOrCreate("musicbrainz-verified-scans", () => Policy(SearchTtl, 128));
        _entities = cacheFactory.GetOrCreate("musicbrainz-entities", () => Policy(EntityTtl, EntityCapacity));
        _missingEntities = cacheFactory.GetOrCreate(
            "musicbrainz-entities-negative",
            () => Policy(MissingEntityTtl, 256));
        _browsePages = cacheFactory.GetOrCreate("musicbrainz-browse-pages", () => Policy(EntityTtl, 256));
        _artistBrowsePages = cacheFactory.GetOrCreate("musicbrainz-artist-browse", () => Policy(EntityTtl, 256));
        _artistResolutions = cacheFactory.GetOrCreate(
            "musicbrainz-artist-resolution",
            () => Policy(SearchTtl, 256));
        _isrcResults = cacheFactory.GetOrCreate("musicbrainz-isrc", () => Policy(SearchTtl, 256));
        _covers = cacheFactory.GetOrCreate("musicbrainz-covers", () => Policy(CoverTtl, 512));
        _missingCovers = cacheFactory.GetOrCreate("musicbrainz-covers-negative", () => Policy(MissingCoverTtl, 256));
    }

    public Task<FetchResult<SearchPage>> GetSearchPageAsync(
        SearchCacheKey key,
        Func<CancellationToken, Task<FetchResult<SearchPage>>> loader,
        CancellationToken cancellationToken)
    {
        return GetCachedAsync(key, _searchPages, _searchLoads, loader, "search", cancellationToken);
    }

    public async Task<FetchResult<VerifiedScan>> GetVerifiedScanAsync(
        string fingerprint,
        Func<VerifiedScan, bool> isSufficient,
        Func<VerifiedScan?, CancellationToken, Task<FetchResult<VerifiedScan>>> loader,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            _verifiedScans.TryGet<VerifiedScan>(fingerprint, out var existing);
            if (existing is not null && isSufficient(existing))
            {
                RecordCache("verified_scan", hit: true);
                return Success(existing);
            }

            RecordCache("verified_scan", hit: false);
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
        var identity = new EntityIdentityKey { Kind = normalizedKey.Kind, Mbid = normalizedKey.Mbid };
        if (_entities.TryGet<CachedEntity>(identity.ToString(), out var cached) &&
            cached is not null &&
            cached.Key.Satisfies(normalizedKey))
        {
            RecordCache("entity", hit: true);
            return Success(cached.Value);
        }

        if (_missingEntities.TryGet<bool>(identity.ToString(), out _))
        {
            RecordCache("entity", hit: true);
            return NotFound<EntityResponseDto>();
        }

        RecordCache("entity", hit: false);
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
        return GetCachedAsync(key, _browsePages, _browseLoads, loader, "browse", cancellationToken);
    }

    public Task<FetchResult<ArtistBrowseResponseDto>> GetArtistBrowsePageAsync(
        ArtistBrowseCacheKey key,
        Func<CancellationToken, Task<FetchResult<ArtistBrowseResponseDto>>> loader,
        CancellationToken cancellationToken)
    {
        return GetCachedAsync(
            key,
            _artistBrowsePages,
            _artistBrowseLoads,
            loader,
            "artist_browse",
            cancellationToken);
    }

    public Task<FetchResult<ArtistResolutionResponse>> GetArtistResolutionAsync(
        ArtistResolutionCacheKey key,
        Func<CancellationToken, Task<FetchResult<ArtistResolutionResponse>>> loader,
        CancellationToken cancellationToken)
    {
        return GetCachedAsync(
            key,
            _artistResolutions,
            _artistResolutionLoads,
            loader,
            "artist_resolution",
            cancellationToken);
    }

    public async Task<FetchResult<SearchPage>> GetIsrcAsync(
        string isrc,
        Func<CancellationToken, Task<FetchResult<SearchPage>>> loader,
        CancellationToken cancellationToken)
    {
        if (_isrcResults.TryGet<SearchPage>(isrc, out var cached) && cached is not null)
        {
            RecordCache("isrc", hit: true);
            return Success(cached);
        }

        var identity = new EntityIdentityKey { Kind = EntityKind.Recording, Mbid = isrc };
        if (_missingEntities.TryGet<bool>(identity.ToString(), out _))
        {
            RecordCache("isrc", hit: true);
            return NotFound<SearchPage>();
        }

        RecordCache("isrc", hit: false);
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
        if (_covers.TryGet<Uri>(key.ToString(), out var cover) && cover is not null)
        {
            RecordCache("cover", hit: true);
            return Success(cover);
        }

        if (_missingCovers.TryGet<bool>(key.ToString(), out _))
        {
            RecordCache("cover", hit: true);
            return NotFound<Uri>();
        }

        RecordCache("cover", hit: false);
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

    public void Dispose()
    {
        _cacheScope.Dispose();
    }

    private static CachePolicy Policy(TimeSpan lifetime, int capacity)
    {
        return new CachePolicy
        {
            Freshness = lifetime,
            Retention = lifetime,
            MaximumEntries = capacity,
            ConcurrencyMode = CacheConcurrencyMode.Pessimistic
        };
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

    private static void RecordCache(string region, bool hit)
    {
        Telemetry.Cache.Add(
            1,
            Telemetry.Provider,
            new KeyValuePair<string, object?>("cache_region", region),
            new KeyValuePair<string, object?>("outcome", hit ? "hit" : "miss"));
    }

    private async Task<FetchResult<TValue>> GetCachedAsync<TKey, TValue>(
        TKey key,
        ICache cache,
        Dictionary<TKey, Task<FetchResult<TValue>>> loads,
        Func<CancellationToken, Task<FetchResult<TValue>>> loader,
        string region,
        CancellationToken cancellationToken)
        where TKey : notnull
        where TValue : class
    {
        var cacheKey = key.ToString()!;
        if (cache.TryGet<TValue>(cacheKey, out var cached) && cached is not null)
        {
            RecordCache(region, hit: true);
            return Success(cached);
        }

        RecordCache(region, hit: false);
        Task<FetchResult<TValue>> shared;
        lock (_loadGate)
        {
            if (!loads.TryGetValue(key, out shared!))
            {
                shared = LoadCachedAsync(key, cacheKey, cache, loads, loader);
                loads[key] = shared;
            }
        }

        return await AwaitAsync(shared, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FetchResult<TValue>> LoadCachedAsync<TKey, TValue>(
        TKey key,
        string cacheKey,
        ICache cache,
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
                cache.Set(cacheKey, value);
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
                _verifiedScans.Set(fingerprint, value);
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
                _entities.Set(identity.ToString(), new CachedEntity(key, value));
            }
            else if (result.Outcome == FetchOutcome.NotFound)
            {
                _missingEntities.Set(identity.ToString(), true);
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
                _isrcResults.Set(isrc, value);
            }
            else if (result.Outcome == FetchOutcome.NotFound)
            {
                _missingEntities.Set(identity.ToString(), true);
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
                _covers.Set(key.ToString(), value);
            }
            else if (result.Outcome == FetchOutcome.NotFound)
            {
                _missingCovers.Set(key.ToString(), true);
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

    private sealed record CachedEntity(EntityCacheKey Key, EntityResponseDto Value);
}
