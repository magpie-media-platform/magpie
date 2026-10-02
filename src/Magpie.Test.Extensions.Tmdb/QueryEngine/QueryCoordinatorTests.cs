using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb;
using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Extensions.Tmdb.QueryEngine;
using Magpie.Sdk.Caching;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tmdb.QueryEngine;

[TestFixture]
public class QueryCoordinatorTests
{
    private ICacheFactory _cacheFactory = null!;

    private IServiceScopeFactory _scopeFactory = null!;

    [SetUp]
    public void SetUp()
    {
        var cacheFactory = new Mock<ICacheFactory>();
        var caches = new Dictionary<string, ICache>(StringComparer.Ordinal);
        cacheFactory.Setup(factory => factory.GetOrCreate(It.IsAny<string>(), It.IsAny<Func<CachePolicy>>()))
            .Returns<string, Func<CachePolicy>?>((name, _) =>
            {
                if (caches.TryGetValue(name, out var existing))
                {
                    return existing;
                }

                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                var cache = new Mock<ICache>();
                cache.Setup(value => value.Set(It.IsAny<string>(), It.IsAny<It.IsAnyType>()))
                    .Callback(new InvocationAction(invocation =>
                        values[(string)invocation.Arguments[0]] = invocation.Arguments[1]));
                cache.Setup(value => value.TryGet(It.IsAny<string>(), out It.Ref<It.IsAnyType?>.IsAny))
                    .Returns(new InvocationFunc(invocation =>
                    {
                        var found = values.TryGetValue((string)invocation.Arguments[0], out var value);
                        ((object?[])invocation.Arguments)[1] = value;
                        return found;
                    }));
                cache.Setup(value => value.Remove(It.IsAny<string>())).Returns<string>(values.Remove);
                cache.Setup(value => value.GetOrAddAsync(
                        It.IsAny<string>(),
                        It.IsAny<Func<CancellationToken, ValueTask<string>>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<string, Func<CancellationToken, ValueTask<string>>, CancellationToken>(
                        async (key, valueFactory, token) =>
                        {
                            if (values.TryGetValue(key, out var cached))
                            {
                                return (string)cached!;
                            }

                            var created = await valueFactory(token).AsTask().WaitAsync(token).ConfigureAwait(false);
                            values[key] = created;
                            return created;
                        });
                cache.Setup(value => value.GetOrAddAsync(
                        It.IsAny<string>(),
                        It.IsAny<Func<CancellationToken, ValueTask<IReadOnlyDictionary<string, int>>>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<
                        string,
                        Func<CancellationToken, ValueTask<IReadOnlyDictionary<string, int>>>,
                        CancellationToken>(
                        async (key, valueFactory, token) =>
                        {
                            if (values.TryGetValue(key, out var cached))
                            {
                                return (IReadOnlyDictionary<string, int>)cached!;
                            }

                            var created = await valueFactory(token).AsTask().WaitAsync(token).ConfigureAwait(false);
                            values[key] = created;
                            return created;
                        });
                cache.Setup(value => value.GetOrAddAsync(
                        It.IsAny<string>(),
                        It.IsAny<Func<CancellationToken, ValueTask<CreditResponse>>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<string, Func<CancellationToken, ValueTask<CreditResponse>>, CancellationToken>(
                        async (key, valueFactory, token) =>
                        {
                            if (values.TryGetValue(key, out var cached))
                            {
                                return (CreditResponse)cached!;
                            }

                            var created = await valueFactory(token).AsTask().WaitAsync(token).ConfigureAwait(false);
                            values[key] = created;
                            return created;
                        });
                return caches[name] = cache.Object;
            });
        _cacheFactory = cacheFactory.Object;
        var provider = new Mock<IServiceProvider>();
        provider.Setup(value => value.GetService(typeof(ICacheFactory))).Returns(_cacheFactory);
        var scope = new Mock<IServiceScope>();
        scope.Setup(value => value.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(value => value.CreateScope()).Returns(scope.Object);
        _scopeFactory = scopeFactory.Object;
    }

    [Test]
    public async Task GetImageBaseUrlAsync_ReturnsAndCachesTheSecureUrl_WhenConfigurationIsValid()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>());
        var context = new RequestContext { ApiKey = "key", Language = "en-US" };
        var client = new Mock<ITmdbClient>();
        var issued = default(Request);
        client.Setup(subject => subject.GetAsync<ConfigurationEnvelopeDto>(
                context,
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => issued = request)
            .ReturnsAsync(new ConfigurationEnvelopeDto
            {
                Images = new ConfigurationImagesDto { SecureBaseUrl = "https://images.example/" }
            });

        // Act
        var first = await cache.GetImageBaseUrlAsync(
            context,
            client.Object,
            NullLogger.Instance,
            default).ConfigureAwait(false);
        var second = await cache.GetImageBaseUrlAsync(
            context,
            client.Object,
            NullLogger.Instance,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo("https://images.example/"));
            Assert.That(second, Is.EqualTo("https://images.example/"));
            Assert.That(issued!.Endpoint, Is.EqualTo("configuration"));
            Assert.That(issued.Path, Is.EqualTo("3/configuration"));
            Assert.That(issued.QueryParameters, Is.Empty);
            Assert.That(issued.CacheKey, Is.EqualTo("3/configuration?"));
            Assert.That(issued.ResponseProfile, Is.EqualTo(ResponseProfile.Configuration));
        }

        client.Verify(subject => subject.GetAsync<ConfigurationEnvelopeDto>(
            context,
            It.IsAny<Request>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetImageBaseUrlAsync_ReturnsFallbackAndRetries_WhenConfigurationOmitsTheSecureUrl()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>());
        var context = new RequestContext { ApiKey = "key", Language = "en-US" };
        var client = new Mock<ITmdbClient>();
        client.Setup(subject => subject.GetAsync<ConfigurationEnvelopeDto>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigurationEnvelopeDto
            {
                Images = new ConfigurationImagesDto { SecureBaseUrl = " " }
            });

        // Act
        var first = await cache.GetImageBaseUrlAsync(
            context,
            client.Object,
            NullLogger.Instance,
            default).ConfigureAwait(false);
        var second = await cache.GetImageBaseUrlAsync(
            context,
            client.Object,
            NullLogger.Instance,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo("https://image.tmdb.org/t/p/"));
            Assert.That(second, Is.EqualTo("https://image.tmdb.org/t/p/"));
        }

        client.Verify(subject => subject.GetAsync<ConfigurationEnvelopeDto>(
            It.IsAny<RequestContext>(),
            It.IsAny<Request>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task GetImageBaseUrlAsync_ThrowsCancellation_WhenTheCallerCancels()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>());
        var context = new RequestContext { ApiKey = "key", Language = "en-US" };
        var client = new Mock<ITmdbClient>();
        var release = new TaskCompletionSource<ConfigurationEnvelopeDto?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.Setup(subject => subject.GetAsync<ConfigurationEnvelopeDto>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Returns(release.Task);
        using var cancellation = new CancellationTokenSource();
        var loading = cache.GetImageBaseUrlAsync(
            context,
            client.Object,
            NullLogger.Instance,
            cancellation.Token);

        // Act
        await cancellation.CancelAsync().ConfigureAwait(false);

        // Assert
        Assert.CatchAsync<OperationCanceledException>(async () => await loading.ConfigureAwait(false));
        release.SetResult(new ConfigurationEnvelopeDto
        {
            Images = new ConfigurationImagesDto { SecureBaseUrl = "https://images.example/" }
        });
    }

    [Test]
    public async Task GetGenreMapAsync_ReturnsAndCachesValidGenres_WhenTheResponseContainsMixedRows()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>());
        var context = new RequestContext { ApiKey = "key", Language = "en-US" };
        var request = new Request
        {
            Endpoint = "movie-genres",
            Path = "3/genre/movie/list",
            QueryParameters = [("language", "en-US")],
            CacheKey = "3/genre/movie/list?language=en-US",
            ResponseProfile = ResponseProfile.Query
        };
        var client = new Mock<ITmdbClient>();
        client.Setup(subject => subject.GetAsync<GenreListEnvelopeDto>(
                context,
                request,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenreListEnvelopeDto
            {
                Genres =
                [
                    new GenreDto { Id = 28, Name = " Action " },
                    new GenreDto { Id = 18, Name = "Drama" },
                    new GenreDto { Id = 0, Name = "Invalid" },
                    new GenreDto { Id = 35, Name = " " }
                ]
            });

        // Act
        var first = await cache.GetGenreMapAsync(context, request, client.Object, default).ConfigureAwait(false);
        var second = await cache.GetGenreMapAsync(context, request, client.Object, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Count.EqualTo(2));
            Assert.That(first["ACTION"], Is.EqualTo(28));
            Assert.That(first["Drama"], Is.EqualTo(18));
            Assert.That(second, Is.SameAs(first));
        }

        client.Verify(subject => subject.GetAsync<GenreListEnvelopeDto>(
            context,
            request,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetGenreMapAsync_ReturnsEmpty_WhenTheResponseHasNoGenreList()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>());
        var context = new RequestContext { ApiKey = "key", Language = "en-US" };
        var request = new Request
        {
            Endpoint = "movie-genres",
            Path = "3/genre/movie/list",
            QueryParameters = [("language", "en-US")],
            CacheKey = "3/genre/movie/list?language=en-US",
            ResponseProfile = ResponseProfile.Query
        };
        var client = new Mock<ITmdbClient>();
        client.Setup(subject => subject.GetAsync<GenreListEnvelopeDto>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenreListEnvelopeDto?)null);

        // Act
        var result = await cache.GetGenreMapAsync(context, request, client.Object, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetCreditsAsync_ReturnsAndCachesTheFirstResponse_WhenTheKeyIsEquivalent()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>());
        var calls = 0;
        var tvFirst = new CreditResponse { Tv = new PersonTvCreditsDto { Cast = [] } };
        var tvSecond = new CreditResponse { Tv = new PersonTvCreditsDto { Crew = [] } };

        // Act
        var first = await cache.GetCreditsAsync(
            "3/person/7/tv_credits?language=en-US",
            RequestRole.TvCredits,
            tvFirst,
            (state, _) =>
            {
                calls++;
                return Task.FromResult(state);
            },
            default).ConfigureAwait(false);
        var second = await cache.GetCreditsAsync(
            "3/person/7/tv_credits?language=en-US",
            RequestRole.TvCredits,
            tvSecond,
            (state, _) =>
            {
                calls++;
                return Task.FromResult(state);
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(first, Is.SameAs(tvFirst));
            Assert.That(second, Is.SameAs(tvFirst));
            Assert.That(second.Tv!.Crew, Is.Null);
        }
    }

    [Test]
    public async Task GetSubjectResolutionsAsync_ReusesTheResult_WhenTheFirstLoadResolved()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>());
        var calls = 0;

        // Act
        var first = await cache.GetSubjectResolutionsAsync(
            "company|contains|regency",
            0,
            (_, _) =>
            {
                calls++;
                return Task.FromResult<IReadOnlyList<SubjectResolution>>(
                [
                    new SubjectResolution
                    {
                        Kind = SubjectResolutionKind.Resolved,
                        Id = 508,
                        Name = "Regency Enterprises"
                    }
                ]);
            },
            default).ConfigureAwait(false);
        var second = await cache.GetSubjectResolutionsAsync(
            "company|contains|regency",
            0,
            (_, _) =>
            {
                calls++;
                return Task.FromResult<IReadOnlyList<SubjectResolution>>([]);
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(second, Is.SameAs(first));
            Assert.That(second.Single().Id, Is.EqualTo(508));
        }
    }

    [Test]
    public async Task GetSubjectResolutionAsync_KeepsTheSharedLoadRunning_WhenOneWaiterCancels()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>());
        var release = new TaskCompletionSource<SubjectResolution>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var first = cache.GetSubjectResolutionAsync(
            "person|alice",
            release,
            static (state, _) =>
            {
                return state.Task;
            },
            default);
        using var cancelled = new CancellationTokenSource();
        var second = cache.GetSubjectResolutionAsync(
            "person|alice",
            release,
            static (state, _) =>
            {
                return state.Task;
            },
            cancelled.Token);

        // Act
        await cancelled.CancelAsync().ConfigureAwait(false);
        Assert.ThrowsAsync<OperationCanceledException>(async () => await second.ConfigureAwait(false));
        release.SetResult(new SubjectResolution
        {
            Kind = SubjectResolutionKind.Resolved,
            Id = 7,
            Name = "Alice"
        });
        var resolved = await first.ConfigureAwait(false);

        // Assert
        Assert.That(resolved.Id, Is.EqualTo(7));
    }

    [Test]
    public async Task GetSubjectResolutionsAsync_CancelsTheLoad_WhenTheOnlyWaiterCancels()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>());
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var loading = cache.GetSubjectResolutionsAsync(
            "person|contains|alice",
            (Started: started, Stopped: stopped),
            static async (state, token) =>
            {
                state.Started.SetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                    return [];
                }
                finally
                {
                    state.Stopped.SetResult(true);
                }
            },
            cancellation.Token);

        // Act
        await started.Task.ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);

        // Assert
        Assert.ThrowsAsync<OperationCanceledException>(async () => await loading.ConfigureAwait(false));
        Assert.That(await stopped.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false), Is.True);
    }

    [Test]
    public async Task GetSubjectResolutionsAsync_KeepsTheSharedLoadRunning_WhenOneWaiterCancels()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>());
        var release = new TaskCompletionSource<IReadOnlyList<SubjectResolution>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var first = cache.GetSubjectResolutionsAsync(
            "person|contains|alice",
            release,
            static (state, _) => state.Task,
            default);
        using var cancellation = new CancellationTokenSource();
        var second = cache.GetSubjectResolutionsAsync(
            "person|contains|alice",
            release,
            static (state, _) => state.Task,
            cancellation.Token);

        // Act
        await cancellation.CancelAsync().ConfigureAwait(false);
        Assert.ThrowsAsync<OperationCanceledException>(async () => await second.ConfigureAwait(false));
        release.SetResult([new SubjectResolution
        {
            Kind = SubjectResolutionKind.Resolved,
            Id = 7,
            Name = "Alice"
        }]);
        var resolved = await first.ConfigureAwait(false);

        // Assert
        Assert.That(resolved.Single().Id, Is.EqualTo(7));
    }

    [Test]
    public void GetSubjectResolutionAsync_RunsTheLoaderAgain_WhenTheFirstLoadFailed()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>());
        var calls = 0;

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(async () => await cache.GetSubjectResolutionAsync(
            "person|alice",
            0,
            (_, _) =>
            {
                calls++;
                return Task.FromException<SubjectResolution>(new InvalidOperationException("boom"));
            },
            default).ConfigureAwait(false));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await cache.GetSubjectResolutionAsync(
            "person|alice",
            0,
            (_, _) =>
            {
                calls++;
                return Task.FromException<SubjectResolution>(new InvalidOperationException("boom"));
            },
            default).ConfigureAwait(false));

        // Assert
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public async Task GetSubjectResolutionAsync_ReusesTheResult_WhenTheSubjectWasNotFound()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>());
        var calls = 0;

        // Act
        await cache.GetSubjectResolutionAsync(
            "person|missing",
            0,
            (_, _) =>
            {
                calls++;
                return Task.FromResult(new SubjectResolution { Kind = SubjectResolutionKind.NotFound });
            },
            default).ConfigureAwait(false);
        await cache.GetSubjectResolutionAsync(
            "person|missing",
            0,
            (_, _) =>
            {
                calls++;
                return Task.FromResult(new SubjectResolution { Kind = SubjectResolutionKind.NotFound });
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void GetScanState_ReturnsTheSameState_WhenTheKeyIsEquivalent()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>());

        // Act
        var first = cache.GetScanState("same-plan");
        first.NextSourcePage = 4;
        var second = cache.GetScanState("same-plan");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(second.NextSourcePage, Is.EqualTo(4));
        }
    }

    [Test]
    public async Task GetSubjectResolutionAsync_CancelsTheLoad_WhenTheApplicationStops()
    {
        // Arrange
        using var application = new CancellationTokenSource();
        var cache = new QueryCoordinator(
            _scopeFactory,
            Mock.Of<IHostApplicationLifetime>(host => host.ApplicationStopping == application.Token));
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loading = cache.GetSubjectResolutionAsync(
            "person|alice",
            started,
            static async (state, token) =>
            {
                state.SetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return new SubjectResolution { Kind = SubjectResolutionKind.NotFound };
            },
            default);

        // Act
        await started.Task.ConfigureAwait(false);
        await application.CancelAsync().ConfigureAwait(false);

        // Assert
        Assert.CatchAsync<OperationCanceledException>(async () => await loading.ConfigureAwait(false));
    }
}
