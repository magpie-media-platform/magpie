using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Sdk.Caching;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed class QueryCoordinator : IQueryCoordinator, IDisposable
{
    private const int GenreCapacity = 32;
    private const int PositiveSubjectCapacity = 512;
    private const int NegativeSubjectCapacity = 256;
    private const int CreditsCapacity = 256;
    private const int ScanCapacity = 128;

    private static readonly Request ConfigurationRequest = new()
    {
        Endpoint = RequestRole.Configuration,
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

    private readonly IServiceScope _cacheScope;

    private readonly ICache _configuration;

    private readonly ICache _genres;

    private readonly ICache _positiveSubjects;

    private readonly ICache _negativeSubjects;

    private readonly ICache _positiveSubjectSets;

    private readonly ICache _negativeSubjectSets;

    private readonly ICache _credits;

    private readonly ICache _scans;
    private readonly Dictionary<string, Task<SubjectResolution>> _subjectInflight = [];
    private readonly Dictionary<string, SharedSubjectSetLoad> _subjectSetInflight = [];
    private readonly object _subjectGate = new();
    private readonly object _scanGate = new();

    private readonly IHostApplicationLifetime _lifetime;

    public QueryCoordinator(IServiceScopeFactory scopeFactory, IHostApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
        _cacheScope = scopeFactory.CreateScope();
        var cacheFactory = _cacheScope.ServiceProvider.GetRequiredService<ICacheFactory>();
        _configuration = cacheFactory.GetOrCreate("tmdb-configuration", () => Policy(ConfigurationTtl, 1));
        _genres = cacheFactory.GetOrCreate("tmdb-genres", () => Policy(GenreTtl, GenreCapacity));
        _positiveSubjects = cacheFactory.GetOrCreate(
            "tmdb-subjects",
            () => Policy(PositiveSubjectTtl, PositiveSubjectCapacity));
        _negativeSubjects = cacheFactory.GetOrCreate(
            "tmdb-subjects-negative",
            () => Policy(NegativeSubjectTtl, NegativeSubjectCapacity));
        _positiveSubjectSets = cacheFactory.GetOrCreate(
            "tmdb-subject-sets",
            () => Policy(PositiveSubjectTtl, PositiveSubjectCapacity));
        _negativeSubjectSets = cacheFactory.GetOrCreate(
            "tmdb-subject-sets-negative",
            () => Policy(NegativeSubjectTtl, NegativeSubjectCapacity));
        _credits = cacheFactory.GetOrCreate("tmdb-credits", () => Policy(CreditsTtl, CreditsCapacity));
        _scans = cacheFactory.GetOrCreate("tmdb-scans", () => Policy(ScanTtl, ScanCapacity));
    }

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
                "configuration",
                QueryCoordinatorRegion.Configuration,
                RequestRole.Configuration,
                async token =>
                {
                    var envelope = await client.GetAsync<ConfigurationEnvelopeDto>(context, ConfigurationRequest, token)
                        .ConfigureAwait(false);
                    var value = envelope?.Images?.SecureBaseUrl;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        throw new InvalidOperationException("TMDb configuration omitted its secure image base URL.");
                    }

                    return value;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TMDb configuration request failed, using default image base URL");
            return Constants.DefaultImageBaseUrl;
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
            request.CacheKey,
            QueryCoordinatorRegion.Genres,
            request.Endpoint,
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
        if (_positiveSubjects.TryGet<SubjectResolution>(key, out var positive))
        {
            RecordCacheOutcome(QueryCoordinatorRegion.Subjects, hit: true);
            return positive!;
        }

        if (_negativeSubjects.TryGet<SubjectResolution>(key, out var negative))
        {
            RecordCacheOutcome(QueryCoordinatorRegion.Subjects, hit: true);
            return negative!;
        }

        RecordCacheOutcome(QueryCoordinatorRegion.Subjects, hit: false);
        var task = default(Task<SubjectResolution>);
        lock (_subjectGate)
        {
            if (!_subjectInflight.TryGetValue(key, out task))
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
        if (_positiveSubjectSets.TryGet<IReadOnlyList<SubjectResolution>>(key, out var positive))
        {
            RecordCacheOutcome(QueryCoordinatorRegion.SubjectSets, hit: true);
            return positive!;
        }

        if (_negativeSubjectSets.TryGet<IReadOnlyList<SubjectResolution>>(key, out var negative))
        {
            RecordCacheOutcome(QueryCoordinatorRegion.SubjectSets, hit: true);
            return negative!;
        }

        RecordCacheOutcome(QueryCoordinatorRegion.SubjectSets, hit: false);
        var shared = default(SharedSubjectSetLoad);
        lock (_subjectGate)
        {
            if (!_subjectSetInflight.TryGetValue(key, out shared) || shared.Cancellation.IsCancellationRequested)
            {
                shared = new SharedSubjectSetLoad(
                    CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping));
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
                if (shared.Waiters == 0 && !shared.Finished)
                {
                    shared.Cancellation.Cancel();
                }
            }
        }
    }

    public Task<CreditResponse> GetCreditsAsync<TState>(
        string key,
        string role,
        TState state,
        Func<TState, CancellationToken, Task<CreditResponse>> loader,
        CancellationToken cancellationToken)
    {
        return GetOrCreateAsync(
            _credits,
            key,
            QueryCoordinatorRegion.Credits,
            role,
            token => loader(state, token),
            cancellationToken);
    }

    public QueryScanState GetScanState(string key)
    {
        lock (_scanGate)
        {
            var cacheMiss = !_scans.TryGet<QueryScanState>(key, out var scan);
            if (cacheMiss)
            {
                scan = new QueryScanState();
                _scans.Set(key, scan);
            }

            RecordCacheOutcome(QueryCoordinatorRegion.Scans, hit: !cacheMiss);

            return scan!;
        }
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

    private static void RecordCacheOutcome(string region, bool hit)
    {
        Telemetry.Cache.Add(
            1,
            Telemetry.ProviderTag,
            new KeyValuePair<string, object?>("cache_region", region),
            hit ? Telemetry.OutcomeHitTag : Telemetry.OutcomeMissTag);
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
            var result = await loader(state, _lifetime.ApplicationStopping).ConfigureAwait(false);
            if (result.Kind == SubjectResolutionKind.Resolved)
            {
                _positiveSubjects.Set(key, result);
            }
            else if (result.Kind == SubjectResolutionKind.NotFound)
            {
                _negativeSubjects.Set(key, result);
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
            if (result.Any(value => value.Kind == SubjectResolutionKind.Resolved) &&
                result.All(value => value.Kind != SubjectResolutionKind.Failed))
            {
                _positiveSubjectSets.Set(key, result);
            }
            else if (result.Count > 0 && result.All(value => value.Kind == SubjectResolutionKind.NotFound))
            {
                _negativeSubjectSets.Set(key, result);
            }

            return result;
        }
        finally
        {
            lock (_subjectGate)
            {
                shared.Finished = true;
                if (_subjectSetInflight.TryGetValue(key, out var current) && ReferenceEquals(current, shared))
                {
                    _subjectSetInflight.Remove(key);
                }
            }

            shared.Cancellation.Dispose();
        }
    }

    private async Task<TValue> GetOrCreateAsync<TValue>(
        ICache cache,
        string key,
        string region,
        string role,
        Func<CancellationToken, Task<TValue>> loader,
        CancellationToken cancellationToken)
    {
        if (cache.TryGet<TValue>(key, out var cached))
        {
            RecordCacheOutcome(region, hit: true);
            Telemetry.Requests.Add(
                1,
                Telemetry.ProviderTag,
                new KeyValuePair<string, object?>("request_role", role),
                Telemetry.CacheHitTag);
            return cached!;
        }

        RecordCacheOutcome(region, hit: false);
        return await cache.GetOrAddAsync(
            key,
            async token => await loader(token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    private sealed class SharedSubjectSetLoad(CancellationTokenSource cancellation)
    {
        internal CancellationTokenSource Cancellation { get; } = cancellation;

        internal Task<IReadOnlyList<SubjectResolution>> Task { get; set; } = null!;

        internal int Waiters { get; set; }

        internal bool Finished { get; set; }
    }
}
