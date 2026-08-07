using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb.Mapping;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed class QueryCache(IHostApplicationLifetime lifetime) : IQueryCache, IDisposable
{
    private const int GenreCapacity = 32;
    private const int PositiveSubjectCapacity = 512;
    private const int NegativeSubjectCapacity = 256;
    private const int CreditsCapacity = 256;
    private const int ScanCapacity = 128;

    private static readonly Request ConfigurationRequest = new()
    {
        Endpoint = "configuration",
        Path = "3/configuration",
        QueryParameters = [],
        CacheKey = "3/configuration?",
        ResponseProfile = ResponseProfile.Configuration
    };

    private static readonly TimeSpan ConfigurationTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan GenreTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan PositiveSubjectTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan NegativeSubjectTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CreditsTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ScanTtl = TimeSpan.FromMinutes(2);

    private readonly MemoryCache _configuration = Bounded(1);
    private readonly MemoryCache _genres = Bounded(GenreCapacity);
    private readonly MemoryCache _positiveSubjects = Bounded(PositiveSubjectCapacity);
    private readonly MemoryCache _negativeSubjects = Bounded(NegativeSubjectCapacity);
    private readonly MemoryCache _positiveSubjectSets = Bounded(PositiveSubjectCapacity);
    private readonly MemoryCache _negativeSubjectSets = Bounded(NegativeSubjectCapacity);
    private readonly MemoryCache _credits = Bounded(CreditsCapacity);
    private readonly MemoryCache _scans = Bounded(ScanCapacity);
    private readonly Dictionary<string, Task<string>> _configurationInflight = [];
    private readonly Dictionary<string, Task<IReadOnlyDictionary<string, int>>> _genreInflight = [];
    private readonly Dictionary<string, Task<SubjectResolution>> _subjectInflight = [];
    private readonly Dictionary<string, SharedSubjectSetLoad> _subjectSetInflight = [];
    private readonly Dictionary<string, Task<CreditResponse>> _creditsInflight = [];
    private readonly object _configurationGate = new();
    private readonly object _genreGate = new();
    private readonly object _subjectGate = new();
    private readonly object _creditsGate = new();
    private readonly object _scanGate = new();

    public async Task<string> GetImageBaseUrlAsync(
        RequestContext context,
        ITmdbClient client,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetOrCreateAsync(
                _configuration,
                ConfigurationTtl,
                _configurationInflight,
                _configurationGate,
                "configuration",
                async token =>
                {
                    var envelope = await client.GetAsync<ConfigurationEnvelopeDto>(context, ConfigurationRequest, token)
                        .ConfigureAwait(false);
                    var value = envelope?.Images?.SecureBaseUrl;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        throw new InvalidOperationException("TMDb configuration omitted its secure image base URL.");
                    }

                    return value!;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TMDb configuration request failed. The cache will use the default image base URL.");
            return TmdbClient.DefaultImageBaseUrl;
        }
    }

    public Task<IReadOnlyDictionary<string, int>> GetGenreMapAsync(
        RequestContext context,
        Request request,
        ITmdbClient client,
        CancellationToken cancellationToken)
    {
        return GetOrCreateAsync<IReadOnlyDictionary<string, int>>(
            _genres,
            GenreTtl,
            _genreInflight,
            _genreGate,
            request.CacheKey,
            async token =>
            {
                var envelope = await client.GetAsync<GenreListEnvelopeDto>(context, request, token)
                    .ConfigureAwait(false);
                var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (envelope?.Genres is not null)
                {
                    foreach (var genre in envelope.Genres.Where(genre =>
                        genre.Id > 0 && !string.IsNullOrWhiteSpace(genre.Name)))
                    {
                        values[genre.Name!.Trim()] = genre.Id;
                    }
                }

                return values;
            },
            cancellationToken);
    }

    public async Task<SubjectResolution> GetSubjectResolutionAsync<TState>(
        string key,
        TState state,
        Func<TState, CancellationToken, Task<SubjectResolution>> loader,
        CancellationToken cancellationToken)
    {
        if (_positiveSubjects.TryGetValue(key, out SubjectResolution? positive))
        {
            return positive!;
        }

        if (_negativeSubjects.TryGetValue(key, out SubjectResolution? negative))
        {
            return negative!;
        }

        var task = default(Task<SubjectResolution>)!;
        lock (_subjectGate)
        {
            if (!_subjectInflight.TryGetValue(key, out task!))
            {
                task = LoadSubjectAsync(key, state, loader);
                _subjectInflight[key] = task;
            }
        }

        return await WaitAsync(task, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SubjectResolution>> GetSubjectResolutionsAsync<TState>(
        string key,
        TState state,
        Func<TState, CancellationToken, Task<IReadOnlyList<SubjectResolution>>> loader,
        CancellationToken cancellationToken)
    {
        if (_positiveSubjectSets.TryGetValue(key, out IReadOnlyList<SubjectResolution>? positive))
        {
            return positive!;
        }

        if (_negativeSubjectSets.TryGetValue(key, out IReadOnlyList<SubjectResolution>? negative))
        {
            return negative!;
        }

        var shared = default(SharedSubjectSetLoad)!;
        lock (_subjectGate)
        {
            if (!_subjectSetInflight.TryGetValue(key, out shared!) || shared.Cancellation.IsCancellationRequested)
            {
                shared = new SharedSubjectSetLoad(
                    CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping));
                _subjectSetInflight[key] = shared;
                shared.Task = LoadSubjectSetAsync(key, state, loader, shared);
            }

            shared.Waiters++;
        }

        try
        {
            return await WaitAsync(shared.Task, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_subjectGate)
            {
                shared.Waiters--;
                if (shared.Waiters == 0 && !shared.Task.IsCompleted)
                {
                    shared.Cancellation.Cancel();
                }
            }
        }
    }

    public Task<CreditResponse> GetCreditsAsync<TState>(
        string key,
        TState state,
        Func<TState, CancellationToken, Task<CreditResponse>> loader,
        CancellationToken cancellationToken)
    {
        return GetOrCreateAsync(
            _credits,
            CreditsTtl,
            _creditsInflight,
            _creditsGate,
            key,
            token => loader(state, token),
            cancellationToken);
    }

    public QueryScanState GetScanState(string key)
    {
        lock (_scanGate)
        {
            return _scans.GetOrCreate(key, entry =>
            {
                entry.Size = 1;
                entry.AbsoluteExpirationRelativeToNow = ScanTtl;
                return new QueryScanState();
            })!;
        }
    }

    public void Dispose()
    {
        _configuration.Dispose();
        _genres.Dispose();
        _positiveSubjects.Dispose();
        _negativeSubjects.Dispose();
        _positiveSubjectSets.Dispose();
        _negativeSubjectSets.Dispose();
        _credits.Dispose();
        _scans.Dispose();
    }

    private static MemoryCache Bounded(int capacity)
    {
        return new MemoryCache(new MemoryCacheOptions { SizeLimit = capacity });
    }

    private static void Set<TValue>(MemoryCache cache, string key, TValue value, TimeSpan ttl)
    {
        cache.Set(
            key,
            value,
            new MemoryCacheEntryOptions
            {
                Size = 1,
                AbsoluteExpirationRelativeToNow = ttl
            });
    }

    private static async Task<TValue> WaitAsync<TValue>(Task<TValue> task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled || task.IsCompleted)
        {
            return await task.ConfigureAwait(false);
        }

        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => cancelled.TrySetResult(true));
        if (task != await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false))
        {
            _ = task.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            throw new OperationCanceledException(cancellationToken);
        }

        return await task.ConfigureAwait(false);
    }

    private async Task<SubjectResolution> LoadSubjectAsync<TState>(
        string key,
        TState state,
        Func<TState, CancellationToken, Task<SubjectResolution>> loader)
    {
        await Task.Yield();
        try
        {
            var result = await loader(state, lifetime.ApplicationStopping).ConfigureAwait(false);
            if (result.Kind == SubjectResolutionKind.Resolved)
            {
                Set(_positiveSubjects, key, result, PositiveSubjectTtl);
            }
            else if (result.Kind == SubjectResolutionKind.NotFound)
            {
                Set(_negativeSubjects, key, result, NegativeSubjectTtl);
            }

            return result;
        }
        finally
        {
            lock (_subjectGate)
            {
                _subjectInflight.Remove(key);
            }
        }
    }

    private async Task<IReadOnlyList<SubjectResolution>> LoadSubjectSetAsync<TState>(
        string key,
        TState state,
        Func<TState, CancellationToken, Task<IReadOnlyList<SubjectResolution>>> loader,
        SharedSubjectSetLoad shared)
    {
        await Task.Yield();
        try
        {
            var result = await loader(state, shared.Cancellation.Token).ConfigureAwait(false);
            if (result.Any(value => value.Kind == SubjectResolutionKind.Resolved))
            {
                Set(_positiveSubjectSets, key, result, PositiveSubjectTtl);
            }
            else if (result.Count > 0 && result.All(value => value.Kind == SubjectResolutionKind.NotFound))
            {
                Set(_negativeSubjectSets, key, result, NegativeSubjectTtl);
            }

            return result;
        }
        finally
        {
            lock (_subjectGate)
            {
                if (_subjectSetInflight.TryGetValue(key, out var current) && ReferenceEquals(current, shared))
                {
                    _subjectSetInflight.Remove(key);
                }
            }

            shared.Cancellation.Dispose();
        }
    }

    private async Task<TValue> GetOrCreateAsync<TValue>(
        MemoryCache cache,
        TimeSpan ttl,
        Dictionary<string, Task<TValue>> inflight,
        object gate,
        string key,
        Func<CancellationToken, Task<TValue>> loader,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(key, out TValue? cached))
        {
            return cached!;
        }

        var task = default(Task<TValue>)!;
        lock (gate)
        {
            if (!inflight.TryGetValue(key, out task!))
            {
                task = LoadAsync(cache, ttl, inflight, gate, key, loader);
                inflight[key] = task;
            }
        }

        return await WaitAsync(task, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TValue> LoadAsync<TValue>(
        MemoryCache cache,
        TimeSpan ttl,
        Dictionary<string, Task<TValue>> inflight,
        object gate,
        string key,
        Func<CancellationToken, Task<TValue>> loader)
    {
        await Task.Yield();
        try
        {
            var value = await loader(lifetime.ApplicationStopping).ConfigureAwait(false);
            Set(cache, key, value, ttl);
            return value;
        }
        finally
        {
            lock (gate)
            {
                inflight.Remove(key);
            }
        }
    }

    private sealed class SharedSubjectSetLoad(CancellationTokenSource cancellation)
    {
        internal CancellationTokenSource Cancellation { get; } = cancellation;

        internal Task<IReadOnlyList<SubjectResolution>> Task { get; set; } = null!;

        internal int Waiters { get; set; }
    }
}
