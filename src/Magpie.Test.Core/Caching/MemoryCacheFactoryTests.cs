using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Core.Caching;
using Magpie.Core.Observability;
using Magpie.Sdk.Caching;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using NUnit.Framework;

namespace Magpie.Test.Core.Caching;

[TestFixture]
public sealed class MemoryCacheFactoryTests
{
    [Test]
    public void ConcurrencyMode_IsOptimistic_WhenPolicyUsesDefaults()
    {
        // Arrange
        var policy = new CachePolicy();

        // Act
        var result = policy.ConcurrencyMode;

        // Assert
        Assert.That(result, Is.EqualTo(CacheConcurrencyMode.Optimistic));
    }

    [Test]
    public void GetOrCreate_ReturnsWorkingCache_WhenPolicyFactoryIsNull()
    {
        // Arrange
        using var store = new MemoryCacheStore(TimeProvider.System);
        using var subject = new MemoryCacheFactory(store);

        // Act
        var cache = subject.GetOrCreate("default");
        cache.Set("value", "stored");
        var found = cache.TryGet<string>("value", out var value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(value, Is.EqualTo("stored"));
        }
    }

    [Test]
    public void GetOrCreate_ReturnsNamedCacheWithoutReevaluatingPolicy_WhenCalledAgain()
    {
        // Arrange
        using var store = new MemoryCacheStore(TimeProvider.System);
        using var subject = new MemoryCacheFactory(store);
        var policyCalls = 0;

        // Act
        var first = subject.GetOrCreate("shared", () =>
        {
            policyCalls++;
            return new CachePolicy();
        });
        var second = subject.GetOrCreate("shared", () =>
        {
            policyCalls++;
            return new CachePolicy { MaximumEntries = 1 };
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(policyCalls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetOrCreate_ReturnsSameCache_WhenCreatedConcurrently()
    {
        // Arrange
        using var store = new MemoryCacheStore(TimeProvider.System);
        using var subject = new MemoryCacheFactory(store);
        var policyCalls = 0;

        // Act
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => subject.GetOrCreate("concurrent", () =>
            {
                Interlocked.Increment(ref policyCalls);
                Thread.Sleep(20);
                return new CachePolicy();
            })))
            .ToArray();
        var caches = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(caches.All(cache => ReferenceEquals(cache, caches[0])), Is.True);
            Assert.That(policyCalls, Is.EqualTo(1));
        }
    }

    [Test]
    public void GetOrCreate_ReturnsSharedCache_WhenResolvedAcrossScopes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<MemoryCacheStore>();
        services.AddScoped<ICacheFactory, MemoryCacheFactory>();
        using var provider = services.BuildServiceProvider();
        ICache first;
        using (var firstScope = provider.CreateScope())
        {
            first = firstScope.ServiceProvider.GetRequiredService<ICacheFactory>().GetOrCreate("shared");
            first.Set("value", "stored");
        }

        // Act
        using var secondScope = provider.CreateScope();
        var second = secondScope.ServiceProvider.GetRequiredService<ICacheFactory>().GetOrCreate("shared");
        var found = second.TryGet<string>("value", out var value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(found, Is.True);
            Assert.That(value, Is.EqualTo("stored"));
        }
    }

    [Test]
    public void CreateScoped_ReturnsIsolatedDisposedCaches_WhenScopeEnds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<MemoryCacheStore>();
        services.AddScoped<ICacheFactory, MemoryCacheFactory>();
        using var provider = services.BuildServiceProvider();
        ICache first;
        ICache second;
        using (var scope = provider.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<ICacheFactory>();
            first = factory.CreateScoped("scope-cache");
            second = factory.CreateScoped("scope-cache");
            first.Set("value", "stored");
        }

        // Act
        using var nextScope = provider.CreateScope();
        var next = nextScope.ServiceProvider.GetRequiredService<ICacheFactory>().CreateScoped("scope-cache");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(next, Is.Not.SameAs(first));
            Assert.That(() => first.TryGet<string>("value", out _), Throws.TypeOf<ObjectDisposedException>());
        }
    }

    [Test]
    public void Set_EvictsLeastRecentlyUsedEntry_WhenCacheIsFull()
    {
        // Arrange
        using var store = new MemoryCacheStore(TimeProvider.System);
        using var subject = new MemoryCacheFactory(store);
        var cache = subject.CreateScoped("lru", new CachePolicy { MaximumEntries = 2 });
        cache.Set("first", "first-value");
        cache.Set("second", "second-value");
        cache.TryGet<string>("first", out _);

        // Act
        cache.Set("third", "third-value");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cache.TryGet<string>("first", out _), Is.True);
            Assert.That(cache.TryGet<string>("second", out _), Is.False);
            Assert.That(cache.TryGet<string>("third", out _), Is.True);
        }
    }

    [Test]
    public void Set_RemovesExpiredEntryBeforeLeastRecentlyUsedLiveEntry_WhenCacheIsFull()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var store = new MemoryCacheStore(timeProvider);
        using var subject = new MemoryCacheFactory(store);
        var cache = subject.CreateScoped(
            "lru-expiration",
            new CachePolicy
            {
                Freshness = TimeSpan.FromMinutes(15),
                Retention = TimeSpan.FromMinutes(15),
                MaximumEntries = 2,
            });
        cache.Set("expired", "expired-value");
        timeProvider.Advance(TimeSpan.FromMinutes(10));
        cache.Set("live", "live-value");
        cache.TryGet<string>("expired", out _);
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        // Act
        cache.Set("new", "new-value");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cache.TryGet<string>("expired", out _), Is.False);
            Assert.That(cache.TryGet<string>("live", out _), Is.True);
            Assert.That(cache.TryGet<string>("new", out _), Is.True);
        }
    }

    [Test]
    public async Task GetOrAddAsync_RunsConcurrentFactories_WhenConcurrencyIsOptimistic()
    {
        // Arrange
        using var store = new MemoryCacheStore(TimeProvider.System);
        using var subject = new MemoryCacheFactory(store);
        var cache = subject.CreateScoped(
            "optimistic",
            new CachePolicy { ConcurrencyMode = CacheConcurrencyMode.Optimistic });
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeFirst = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeSecond = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        var first = cache.GetOrAddAsync(
            "value",
            async _ =>
            {
                firstStarted.SetResult();
                return await completeFirst.Task.ConfigureAwait(false);
            }).AsTask();
        await firstStarted.Task.ConfigureAwait(false);
        var second = cache.GetOrAddAsync(
            "value",
            async _ =>
            {
                secondStarted.SetResult();
                return await completeSecond.Task.ConfigureAwait(false);
            }).AsTask();
        await secondStarted.Task.ConfigureAwait(false);
        completeSecond.SetResult("second");
        await second.ConfigureAwait(false);
        completeFirst.SetResult("first");
        await first.ConfigureAwait(false);
        var found = cache.TryGet<string>("value", out var value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(value, Is.EqualTo("first"));
        }
    }

    [Test]
    public async Task GetOrAddAsync_SharesConcurrentFactory_WhenConcurrencyIsPessimistic()
    {
        // Arrange
        using var store = new MemoryCacheStore(TimeProvider.System);
        using var subject = new MemoryCacheFactory(store);
        var cache = subject.CreateScoped(
            "pessimistic",
            new CachePolicy { ConcurrencyMode = CacheConcurrencyMode.Pessimistic });
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;
        Func<CancellationToken, ValueTask<string>> factory = async _ =>
        {
            Interlocked.Increment(ref factoryCalls);
            started.SetResult();
            return await complete.Task.ConfigureAwait(false);
        };

        // Act
        var first = cache.GetOrAddAsync("value", factory).AsTask();
        await started.Task.ConfigureAwait(false);
        var second = cache.GetOrAddAsync("value", factory).AsTask();
        complete.SetResult("shared");
        var values = await Task.WhenAll(first, second).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(values, Is.EqualTo(["shared", "shared"]));
        }
    }

    [Test]
    public async Task GetOrAddAsync_KeepsSharedFactoryRunning_WhenOnePessimisticWaiterCancels()
    {
        // Arrange
        using var store = new MemoryCacheStore(TimeProvider.System);
        using var subject = new MemoryCacheFactory(store);
        var cache = subject.CreateScoped(
            "pessimistic-cancellation",
            new CachePolicy { ConcurrencyMode = CacheConcurrencyMode.Pessimistic });
        using var firstCancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken factoryCancellation = default;
        var factoryCalls = 0;
        async ValueTask<string> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            factoryCancellation = cancellationToken;
            started.SetResult();
            return await complete.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // Act
        var first = cache.GetOrAddAsync("value", Factory, firstCancellation.Token).AsTask();
        await started.Task.ConfigureAwait(false);
        var second = cache.GetOrAddAsync("value", Factory).AsTask();
        await firstCancellation.CancelAsync().ConfigureAwait(false);
        Assert.CatchAsync<OperationCanceledException>(async () => await first.ConfigureAwait(false));
        complete.SetResult("shared");
        var value = await second.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(factoryCancellation.IsCancellationRequested, Is.False);
            Assert.That(value, Is.EqualTo("shared"));
        }
    }

    [Test]
    public async Task GetOrAddAsync_CancelsSharedFactory_WhenAllPessimisticWaitersCancel()
    {
        // Arrange
        using var store = new MemoryCacheStore(TimeProvider.System);
        using var subject = new MemoryCacheFactory(store);
        var cache = subject.CreateScoped(
            "pessimistic-abandonment",
            new CachePolicy { ConcurrencyMode = CacheConcurrencyMode.Pessimistic });
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<string> Factory(CancellationToken cancellationToken)
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return "unreachable";
            }
            catch (OperationCanceledException)
            {
                factoryCanceled.SetResult();
                throw;
            }
        }

        // Act
        var first = cache.GetOrAddAsync("value", Factory, firstCancellation.Token).AsTask();
        await started.Task.ConfigureAwait(false);
        var second = cache.GetOrAddAsync("value", Factory, secondCancellation.Token).AsTask();
        await firstCancellation.CancelAsync().ConfigureAwait(false);
        Assert.CatchAsync<OperationCanceledException>(async () => await first.ConfigureAwait(false));
        await secondCancellation.CancelAsync().ConfigureAwait(false);
        Assert.CatchAsync<OperationCanceledException>(async () => await second.ConfigureAwait(false));
        await factoryCanceled.Task.ConfigureAwait(false);
        var value = await cache.GetOrAddAsync(
            "value",
            _ => ValueTask.FromResult("recovered")).ConfigureAwait(false);

        // Assert
        Assert.That(value, Is.EqualTo("recovered"));
    }

    [Test]
    public async Task GetOrAddAsync_StartsReplacement_WhenAbandonedPessimisticFactoryIgnoresCancellation()
    {
        // Arrange
        using var store = new MemoryCacheStore(TimeProvider.System);
        using var subject = new MemoryCacheFactory(store);
        var cache = subject.CreateScoped(
            "pessimistic-zombie",
            new CachePolicy { ConcurrencyMode = CacheConcurrencyMode.Pessimistic });
        using var cancellation = new CancellationTokenSource();
        var abandonedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var abandonedFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAbandoned = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<string> AbandonedFactory(CancellationToken _)
        {
            abandonedStarted.SetResult();
            try
            {
                return await releaseAbandoned.Task.ConfigureAwait(false);
            }
            finally
            {
                abandonedFinished.SetResult();
            }
        }

        // Act
        var abandoned = cache.GetOrAddAsync("value", AbandonedFactory, cancellation.Token).AsTask();
        await abandonedStarted.Task.ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        Assert.CatchAsync<OperationCanceledException>(async () => await abandoned.ConfigureAwait(false));
        var replacement = await cache.GetOrAddAsync(
            "value",
            _ => ValueTask.FromResult("replacement")).ConfigureAwait(false);
        releaseAbandoned.SetResult("abandoned");
        await abandonedFinished.Task.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(replacement, Is.EqualTo("replacement"));
            Assert.That(cache.TryGet<string>("value", out var cached), Is.True);
            Assert.That(cached, Is.EqualTo("replacement"));
        }
    }

    [Test]
    public async Task GetOrAddAsync_StoresValueTypes_WhenCacheIsUsed()
    {
        // Arrange
        using var store = new MemoryCacheStore(TimeProvider.System);
        using var subject = new MemoryCacheFactory(store);
        var cache = subject.CreateScoped("value-types");
        var expectedGuid = Guid.NewGuid();
        var factoryCalls = 0;

        // Act
        var missing = cache.TryGet<int>("int", out _);
        var created = await cache.GetOrAddAsync(
            "int",
            _ =>
            {
                factoryCalls++;
                return ValueTask.FromResult(42);
            }).ConfigureAwait(false);
        var reused = await cache.GetOrAddAsync(
            "int",
            _ =>
            {
                factoryCalls++;
                return ValueTask.FromResult(99);
            }).ConfigureAwait(false);
        cache.Set("guid", expectedGuid);
        cache.Set("struct", (Left: 7, Right: 9));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing, Is.False);
            Assert.That(created, Is.EqualTo(42));
            Assert.That(reused, Is.EqualTo(42));
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(cache.TryGet<Guid>("guid", out var guid), Is.True);
            Assert.That(guid, Is.EqualTo(expectedGuid));
            Assert.That(cache.TryGet<(int Left, int Right)>("struct", out var value), Is.True);
            Assert.That(value, Is.EqualTo((7, 9)));
        }
    }

    [Test]
    public void Remove_RemovesValue_WhenValueExists()
    {
        // Arrange
        using var store = new MemoryCacheStore(TimeProvider.System);
        using var subject = new MemoryCacheFactory(store);
        var cache = subject.CreateScoped("remove");
        cache.Set("value", "stored");

        // Act
        var removed = cache.Remove("value");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed, Is.True);
            Assert.That(cache.TryGet<string>("value", out _), Is.False);
            Assert.That(cache.Remove("value"), Is.False);
        }
    }

    [Test]
    public void Set_StoresNull_WhenReferenceValueIsNull()
    {
        // Arrange
        using var store = new MemoryCacheStore(TimeProvider.System);
        using var subject = new MemoryCacheFactory(store);
        var cache = subject.CreateScoped("null");

        // Act
        cache.Set<string?>("value", null);
        var found = cache.TryGet<string?>("value", out var value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(value, Is.Null);
        }
    }

    [Test]
    public void Set_DoesNotExpireValue_WhenPolicyLifetimeIsInfinite()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var store = new MemoryCacheStore(timeProvider);
        using var subject = new MemoryCacheFactory(store);
        var cache = subject.CreateScoped(
            "infinite",
            new CachePolicy
            {
                Freshness = Timeout.InfiniteTimeSpan,
                Retention = Timeout.InfiniteTimeSpan,
            });
        cache.Set("value", "stored");

        // Act
        timeProvider.Advance(TimeSpan.FromDays(36500));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cache.TryGet<string>("value", out var value), Is.True);
            Assert.That(value, Is.EqualTo("stored"));
        }
    }

    [TestCase(CacheConcurrencyMode.Optimistic)]
    [TestCase(CacheConcurrencyMode.Pessimistic)]
    public async Task GetOrAddAsync_DoesNotCacheFailure_WhenFactoryThrows(CacheConcurrencyMode concurrencyMode)
    {
        // Arrange
        using var store = new MemoryCacheStore(TimeProvider.System);
        using var subject = new MemoryCacheFactory(store);
        var cache = subject.CreateScoped("failure", new CachePolicy { ConcurrencyMode = concurrencyMode });

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(async () => await cache.GetOrAddAsync(
            "value",
            _ => ValueTask.FromException<string>(new InvalidOperationException("failed"))).ConfigureAwait(false));
        var value = await cache.GetOrAddAsync(
            "value",
            _ => ValueTask.FromResult("recovered")).ConfigureAwait(false);

        // Assert
        Assert.That(value, Is.EqualTo("recovered"));
    }

    [TestCase(CacheConcurrencyMode.Optimistic)]
    [TestCase(CacheConcurrencyMode.Pessimistic)]
    public async Task GetOrAddAsync_DoesNotCacheCancellation_WhenFactoryIsCanceled(
        CacheConcurrencyMode concurrencyMode)
    {
        // Arrange
        using var store = new MemoryCacheStore(TimeProvider.System);
        using var subject = new MemoryCacheFactory(store);
        var cache = subject.CreateScoped("cancellation", new CachePolicy { ConcurrencyMode = concurrencyMode });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);

        // Act
        Assert.CatchAsync<OperationCanceledException>(async () => await cache.GetOrAddAsync(
            "value",
            token => ValueTask.FromCanceled<string>(token),
            cancellation.Token).ConfigureAwait(false));
        var value = await cache.GetOrAddAsync(
            "value",
            _ => ValueTask.FromResult("recovered")).ConfigureAwait(false);

        // Assert
        Assert.That(value, Is.EqualTo("recovered"));
    }

    [Test]
    public void TryGet_RecordsHitAndMissWithoutKey_WhenMemoryCacheUsed()
    {
        // Arrange
        var measurements = new List<(
            string Name,
            long Value,
            KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == DataTelemetry.Name &&
                instrument.Name is "magpie.cache.hit" or "magpie.cache.miss")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var capturedTags = tags.ToArray();
            if (capturedTags.Any(tag => tag.Key == "cache" &&
                tag.Value is "telemetry" or "scoped-telemetry"))
            {
                measurements.Add((instrument.Name, value, capturedTags));
            }
        });
        listener.Start();
        using var store = new MemoryCacheStore(TimeProvider.System);
        using var subject = new MemoryCacheFactory(store);
        var cache = subject.GetOrCreate("telemetry");

        // Act
        cache.TryGet<string>("secret-key", out _);
        cache.Set("secret-key", "secret-value");
        cache.TryGet<string>("secret-key", out _);
        var scoped = subject.CreateScoped("scoped-telemetry");
        scoped.TryGet<string>("private-key", out _);
        scoped.Set("private-key", "private-value");
        scoped.TryGet<string>("private-key", out _);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurements.Select(value => value.Name),
                Is.EqualTo([
                    "magpie.cache.miss",
                    "magpie.cache.hit",
                    "magpie.cache.miss",
                    "magpie.cache.hit",
                ]));
            Assert.That(measurements.All(value => value.Value == 1), Is.True);
            Assert.That(measurements.Take(2).All(value => value.Tags.Any(tag =>
                tag.Key == "cache" && Equals(tag.Value, "telemetry"))), Is.True);
            Assert.That(measurements.Skip(2).All(value => value.Tags.Any(tag =>
                tag.Key == "cache" && Equals(tag.Value, "scoped-telemetry"))), Is.True);
            Assert.That(measurements.All(value => value.Tags.Any(tag =>
                tag.Key == "backend" && Equals(tag.Value, "memory"))), Is.True);
            Assert.That(measurements.All(value => value.Tags.Any(tag =>
                tag.Key == "level" && Equals(tag.Value, "memory"))), Is.True);
            Assert.That(measurements.Take(2).All(value => value.Tags.Any(tag =>
                tag.Key == "lifetime" && Equals(tag.Value, "shared"))), Is.True);
            Assert.That(measurements.Skip(2).All(value => value.Tags.Any(tag =>
                tag.Key == "lifetime" && Equals(tag.Value, "private"))), Is.True);
            Assert.That(measurements.SelectMany(value => value.Tags).Any(tag =>
                Equals(tag.Value, "secret-key") ||
                Equals(tag.Value, "secret-value") ||
                Equals(tag.Value, "private-key") ||
                Equals(tag.Value, "private-value")), Is.False);
        }
    }
}
