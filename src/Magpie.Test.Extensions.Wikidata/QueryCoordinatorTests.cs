using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata;
using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models.Enums;
using Magpie.Sdk.Caching;
using Magpie.Sdk.Http;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

using RichardSzalay.MockHttp;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
[NonParallelizable]
internal sealed class QueryCoordinatorTests
{
    private ServiceProvider _cooldownServices = null!;

    private Mock<ICacheFactory> _cacheFactory = null!;

    private IServiceScopeFactory _scopeFactory = null!;

    [SetUp]
    public void SetUp()
    {
        _cooldownServices = new ServiceCollection().AddHttpClient("test-cooldown")
            .AddUpstreamCooldown().Services.BuildServiceProvider();
        _cacheFactory = new Mock<ICacheFactory>();
        _cacheFactory.Setup(factory => factory.GetOrCreate(It.IsAny<string>(), It.IsAny<Func<CachePolicy>>()))
            .Returns(() =>
            {
                var values = new Dictionary<string, object>();
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
                return cache.Object;
            });
        var provider = new Mock<IServiceProvider>();
        provider.Setup(value => value.GetService(typeof(ICacheFactory))).Returns(() => _cacheFactory.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(value => value.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(value => value.CreateScope()).Returns(scope.Object);
        _scopeFactory = scopeFactory.Object;
    }

    [TearDown]
    public void DisposeCooldownServices() => _cooldownServices.Dispose();

    [Test]
    public async Task GetEntitiesAsync_CompletesEveryBatch_WhenPendingIdsExceedCacheCapacity()
    {
        // Arrange
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>());
        var subject = new QueryCoordinator(client.Object, _scopeFactory);
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var ids = Enumerable.Range(1, 1025).Select(value => "Q" + value).ToArray();

        // Act
        var result = await subject.GetEntitiesAsync(ids, context, default)
            .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
        client.Verify(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            context,
            It.IsAny<CancellationToken>()), Times.Exactly(21));
    }

    [TestCase(false, false, false, 3)]
    [TestCase(true, false, false, 3)]
    [TestCase(false, true, false, 3)]
    [TestCase(true, true, false, 3)]
    [TestCase(false, false, true, 1)]
    [TestCase(true, false, true, 1)]
    [TestCase(false, true, true, 1)]
    [TestCase(true, true, true, 4)]
    public async Task SearchAsync_ReportsEverySharedRequest_WhenWaitersCancelOrUpstreamThrottles(
        bool cancelOwner, bool cancelFollower, bool retry, int expectedRequests)
    {
        // Arrange
        var snapshots = new ConcurrentQueue<(string? Reason, int Requests)>();
        using var trace = new Activity("shared-request-accounting-test").Start();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "magpie.extensions.wikidata",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> options) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "wikidata.query.execute" && activity.TraceId == trace.TraceId)
                {
                    snapshots.Enqueue((
                        activity.GetTagItem("magpie.query.terminal_reason") as string,
                        (int)(activity.GetTagItem("magpie.query.request_count") ?? 0)));
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = 0;
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
            .Respond(_ =>
            {
                Interlocked.Increment(ref sends);
                firstStarted.SetResult(true);
                return firstResponse.Task;
            });
        if (retry)
        {
            http.Expect(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
                .Respond(_ =>
                {
                    Interlocked.Increment(ref sends);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"search\":[],\"search-continue\":50}")
                    };
                });
        }

        http.Expect(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
            .Respond(_ =>
            {
                Interlocked.Increment(ref sends);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"search\":[]}")
                };
            });
        http.Expect(HttpMethod.Post, WikidataClient.DefaultSparqlUrl)
            .Respond(_ =>
            {
                Interlocked.Increment(ref sends);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"results\":{\"bindings\":[]}}")
                };
            });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(WikidataClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        var client = new WikidataClient(
            factory.Object,
            _cooldownServices.GetRequiredService<IUpstreamCooldown>(),
            TimeProvider.System);
        using var coordinator = new QueryCoordinator(client, _scopeFactory);
        var subject = new QueryExecutor(client, coordinator, NullLogger<QueryExecutor>.Instance);
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = CriteriaValueOperator.StartsWithString,
                    Values = ["Shared lookup"]
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        using var cancellation = new CancellationTokenSource();
        using var followerCancellation = new CancellationTokenSource();

        // Act
        var owner = subject.SearchAsync(plan, context, cancellation.Token);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        var follower = subject.SearchAsync(plan, context, followerCancellation.Token);
        if (cancelOwner)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.That(async () => await owner.ConfigureAwait(false), Throws.TypeOf<OperationCanceledException>());
        }

        if (cancelFollower)
        {
            await followerCancellation.CancelAsync().ConfigureAwait(false);
            Assert.That(async () => await follower.ConfigureAwait(false), Throws.TypeOf<OperationCanceledException>());
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(retry
                ? "{\"error\":{\"code\":\"maxlag\"}}"
                : "{\"search\":[],\"search-continue\":50}")
        };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
        firstResponse.SetResult(response);
        var survivingQuery = cancelOwner && cancelFollower
            ? subject.SearchAsync(plan, context, default)
            : cancelFollower ? owner : follower;
        if (retry && !(cancelOwner && cancelFollower))
        {
            // Assert
            Assert.ThrowsAsync<Magpie.Models.UpstreamThrottledException>(async () =>
                await survivingQuery.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
            if (!cancelOwner)
            {
                Assert.ThrowsAsync<Magpie.Models.UpstreamThrottledException>(async () =>
                    await owner.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
            }

            Assert.ThrowsAsync<Magpie.Models.UpstreamThrottledException>(async () =>
                await subject.SearchAsync(plan, context, default).ConfigureAwait(false));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sends, Is.EqualTo(1));
                Assert.That(snapshots.Sum(snapshot => snapshot.Requests), Is.EqualTo(1));
            }

            return;
        }

        var result = await survivingQuery
            .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        if (!cancelOwner)
        {
            await owner.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        var cached = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(cached.Hits, Is.Empty);
            Assert.That(sends, Is.EqualTo(expectedRequests));
            Assert.That(snapshots, Has.Count.EqualTo(cancelOwner && cancelFollower ? 4 : 3));
            Assert.That(snapshots.Sum(snapshot => snapshot.Requests), Is.EqualTo(expectedRequests));
            Assert.That(snapshots.Last(), Is.EqualTo(("completed", 0)));
            Assert.That(snapshots.Count(snapshot => snapshot.Reason == "cancelled"),
                Is.EqualTo(cancelOwner && cancelFollower ? 2 : cancelOwner || cancelFollower ? 1 : 0));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetSearchAsync_ReturnsToEveryWaiter_WhenCacheDoesNotRetainValues()
    {
        // Arrange
        _cacheFactory.Setup(factory => factory.GetOrCreate(It.IsAny<string>(), It.IsAny<Func<CachePolicy>>()))
            .Returns(Mock.Of<ICache>());
        var subject = new QueryCoordinator(Mock.Of<IWikidataClient>(), _scopeFactory);
        var source = new TaskCompletionSource<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = subject.GetSearchAsync("key", _ => source.Task, default);
        var follower = subject.GetSearchAsync("key", _ => source.Task, default);

        // Act
        source.SetResult([]);
        var results = await Task.WhenAll(owner, follower).WaitAsync(TimeSpan.FromSeconds(5))
            .ConfigureAwait(false);
        var next = await subject.GetSearchAsync("key", _ => source.Task, default)
            .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results[0].Rows, Is.Empty);
            Assert.That(results[1].Rows, Is.Empty);
            Assert.That(results[0].Provenance, Is.EqualTo(SearchCacheProvenance.Remote));
            Assert.That(results[1].Provenance, Is.EqualTo(SearchCacheProvenance.InFlight));
            Assert.That(next.Provenance, Is.EqualTo(SearchCacheProvenance.Remote));
        }
    }

    [Test]
    public async Task GetIdentityAsync_Coalesce_WhenConcurrentMisses()
    {
        // Arrange
        var subject = new QueryCoordinator(Mock.Of<IWikidataClient>(), _scopeFactory);
        var gate = new TaskCompletionSource<EntityResolution>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        // Act
        var first = subject.GetIdentityAsync(
            "same",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return gate.Task;
            },
            default);
        var second = subject.GetIdentityAsync(
            "same",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return gate.Task;
            },
            default);
        gate.SetResult(new EntityResolution
        {
            Kind = EntityResolutionKind.Resolved,
            Qid = "Q1"
        });
        var results = await Task.WhenAll(first, second).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(results.Select(result => result.Qid), Is.All.EqualTo("Q1"));
        }
    }

    [Test]
    public async Task GetIdentityAsync_DoesNotCancelSharedWork_WhenCancelledWaiter()
    {
        // Arrange
        var subject = new QueryCoordinator(Mock.Of<IWikidataClient>(), _scopeFactory);
        var gate = new TaskCompletionSource<EntityResolution>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var first = subject.GetIdentityAsync("same", _ => gate.Task, cancellation.Token);
        var second = subject.GetIdentityAsync("same", _ => gate.Task, default);

        // Act
        await cancellation.CancelAsync().ConfigureAwait(false);
        gate.SetResult(new EntityResolution
        {
            Kind = EntityResolutionKind.Resolved,
            Qid = "Q2"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(async () => await first.ConfigureAwait(false), Throws.TypeOf<OperationCanceledException>());
            Assert.That((await second.ConfigureAwait(false)).Qid, Is.EqualTo("Q2"));
        }
    }

    [Test]
    public void GetSearchAsync_DoesNotLaunchFill_WhenAlreadyCancelledOwner()
    {
        // Arrange
        var subject = new QueryCoordinator(Mock.Of<IWikidataClient>(), _scopeFactory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;

        // Act
        var action = async () => await subject.GetSearchAsync(
            "cancelled-owner",
            _ =>
            {
                calls++;
                return Task.FromResult<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>([]);
            },
            cancellation.Token).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(action, Throws.TypeOf<OperationCanceledException>());
            Assert.That(calls, Is.Zero);
        }
    }

    [Test]
    public async Task GetSearchAsync_CancelsFillAndDoesNotCacheCompletion_WhenCancelledOnlyOwner()
    {
        // Arrange
        var subject = new QueryCoordinator(Mock.Of<IWikidataClient>(), _scopeFactory);
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fillCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var first = subject.GetSearchAsync(
            "cancelled-fill",
            async token =>
            {
                calls++;
                started.TrySetResult(true);
                using var registration = token.Register(() => fillCancelled.TrySetResult(true));
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return [];
            },
            cancellation.Token);
        await started.Task.ConfigureAwait(false);

        // Act
        await cancellation.CancelAsync().ConfigureAwait(false);
        await fillCancelled.Task.ConfigureAwait(false);
        var second = await subject.GetSearchAsync(
            "cancelled-fill",
            _ =>
            {
                calls++;
                return Task.FromResult<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>([]);
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(async () => await first.ConfigureAwait(false), Throws.TypeOf<OperationCanceledException>());
            Assert.That(second.Provenance, Is.EqualTo(SearchCacheProvenance.Remote));
            Assert.That(calls, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetSearchAsync_CancelsFillOnlyAfterBothCancel_WhenOwnerAndJoiner()
    {
        // Arrange
        var subject = new QueryCoordinator(Mock.Of<IWikidataClient>(), _scopeFactory);
        using var ownerCancellation = new CancellationTokenSource();
        using var joinerCancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fillCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = subject.GetSearchAsync(
            "shared-lifetime",
            async token =>
            {
                started.TrySetResult(true);
                using var registration = token.Register(() => fillCancelled.TrySetResult(true));
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return [];
            },
            ownerCancellation.Token);
        await started.Task.ConfigureAwait(false);
        var joiner = subject.GetSearchAsync(
            "shared-lifetime",
            _ => Task.FromResult<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>([]),
            joinerCancellation.Token);

        // Act
        await ownerCancellation.CancelAsync().ConfigureAwait(false);
        var fillAfterOwnerCancellation = fillCancelled.Task.IsCompleted;
        await joinerCancellation.CancelAsync().ConfigureAwait(false);
        await fillCancelled.Task.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fillAfterOwnerCancellation, Is.False);
            Assert.That(async () => await owner.ConfigureAwait(false), Throws.TypeOf<OperationCanceledException>());
            Assert.That(async () => await joiner.ConfigureAwait(false), Throws.TypeOf<OperationCanceledException>());
        }
    }

    [Test]
    public void GetSearchAsync_IsNotCached_WhenFailure()
    {
        // Arrange
        var subject = new QueryCoordinator(Mock.Of<IWikidataClient>(), _scopeFactory);
        var calls = 0;

        // Act
        var first = async () => await subject.GetSearchAsync(
            "failed",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromException<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>(
                    new HttpRequestException("failed"));
            },
            default).ConfigureAwait(false);
        var second = async () => await subject.GetSearchAsync(
            "failed",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromException<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>(
                    new HttpRequestException("failed"));
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Throws.TypeOf<HttpRequestException>());
            Assert.That(second, Throws.TypeOf<HttpRequestException>());
            Assert.That(calls, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetSearchAsync_ReturnsRemoteProvenanceAndFactoryRows_WhenOwner()
    {
        // Arrange
        var subject = new QueryCoordinator(Mock.Of<IWikidataClient>(), _scopeFactory);
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new() { Type = "uri", Value = "http://www.wikidata.org/entity/Q1" }
            }
        ];

        // Act
        var result = await subject.GetSearchAsync("remote", _ => Task.FromResult(rows), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Provenance, Is.EqualTo(SearchCacheProvenance.Remote));
            Assert.That(result.Rows, Is.SameAs(rows));
        }
    }

    [Test]
    public async Task GetSearchAsync_ReturnsCacheProvenanceAndFactoryRows_WhenCompletedEntry()
    {
        // Arrange
        var subject = new QueryCoordinator(Mock.Of<IWikidataClient>(), _scopeFactory);
        var calls = 0;
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new() { Type = "uri", Value = "http://www.wikidata.org/entity/Q2" }
            }
        ];
        _ = await subject.GetSearchAsync(
            "cached",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(rows);
            },
            default).ConfigureAwait(false);

        // Act
        var result = await subject.GetSearchAsync(
            "cached",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(rows);
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Provenance, Is.EqualTo(SearchCacheProvenance.Cache));
            Assert.That(result.Rows, Is.SameAs(rows));
            Assert.That(calls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetSearchAsync_ReturnsInFlightProvenanceAndFactoryRows_WhenPendingEntry()
    {
        // Arrange
        var subject = new QueryCoordinator(Mock.Of<IWikidataClient>(), _scopeFactory);
        var gate = new TaskCompletionSource<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new() { Type = "uri", Value = "http://www.wikidata.org/entity/Q3" }
            }
        ];
        SearchCacheProvenance? observedProvenance = null;

        // Act
        var owner = subject.GetSearchAsync(
            "in-flight",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return gate.Task;
            },
            default);
        var follower = subject.GetSearchAsync(
            "in-flight",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return gate.Task;
            },
            default,
            value => observedProvenance = value);
        var observedBeforeCompletion = observedProvenance;
        gate.SetResult(rows);
        var ownerResult = await owner.ConfigureAwait(false);
        var followerResult = await follower.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ownerResult.Provenance, Is.EqualTo(SearchCacheProvenance.Remote));
            Assert.That(followerResult.Provenance, Is.EqualTo(SearchCacheProvenance.InFlight));
            Assert.That(observedBeforeCompletion, Is.EqualTo(SearchCacheProvenance.InFlight));
            Assert.That(ownerResult.Rows, Is.SameAs(rows));
            Assert.That(followerResult.Rows, Is.SameAs(rows));
            Assert.That(calls, Is.EqualTo(1));
        }
    }

    [TestCase(false, false, false, 2, 0)]
    [TestCase(true, false, false, 1, 1)]
    [TestCase(false, true, false, 2, 0)]
    [TestCase(true, false, true, 1, 1)]
    public async Task GetEntitiesAsync_CountsEachPhysicalBatchOnce_WhenWaitersShareHydration(
        bool cancelOwner, bool cancelFollower, bool partialFollower, int expectedOwner, int expectedFollower)
    {
        // Arrange
        var ownerRequests = 0;
        var followerRequests = 0;
        var sends = 0;
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
            .Respond(_ =>
            {
                Interlocked.Increment(ref sends);
                firstStarted.SetResult(true);
                return firstResponse.Task;
            });
        http.Expect(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
            .WithQueryString("ids", "Q51")
            .Respond(_ =>
            {
                Interlocked.Increment(ref sends);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"entities\":{\"Q51\":{\"id\":\"Q51\"}}}")
                };
            });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(WikidataClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        var client = new WikidataClient(
            factory.Object,
            _cooldownServices.GetRequiredService<IUpstreamCooldown>(),
            TimeProvider.System);
        using var subject = new QueryCoordinator(client, _scopeFactory);
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test",
            UpstreamRequestObserver = () => Interlocked.Increment(ref ownerRequests)
        };
        var followerContext = context with
        {
            UpstreamRequestObserver = () => Interlocked.Increment(ref followerRequests)
        };
        var ids = Enumerable.Range(1, 51).Select(value => "Q" + value).ToArray();
        using var ownerCancellation = new CancellationTokenSource();
        using var followerCancellation = new CancellationTokenSource();

        // Act
        var owner = subject.GetEntitiesAsync(ids, context, ownerCancellation.Token);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        var follower = subject.GetEntitiesAsync(
            partialFollower ? ["Q51"] : ids, followerContext, followerCancellation.Token);
        if (cancelOwner)
        {
            await ownerCancellation.CancelAsync().ConfigureAwait(false);
            Assert.That(async () => await owner.ConfigureAwait(false), Throws.TypeOf<OperationCanceledException>());
        }

        if (cancelFollower)
        {
            await followerCancellation.CancelAsync().ConfigureAwait(false);
            Assert.That(async () => await follower.ConfigureAwait(false), Throws.TypeOf<OperationCanceledException>());
        }

        firstResponse.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"entities\":{}}")
        });
        var result = await (cancelFollower ? owner : follower)
            .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        if (!cancelOwner)
        {
            await owner.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        var cached = await subject.GetEntitiesAsync(["Q51"], followerContext, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result["Q51"].Id, Is.EqualTo("Q51"));
            Assert.That(cached["Q51"].Id, Is.EqualTo("Q51"));
            Assert.That(sends, Is.EqualTo(2));
            Assert.That(ownerRequests, Is.EqualTo(expectedOwner));
            Assert.That(followerRequests, Is.EqualTo(expectedFollower));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetEntitiesAsync_ChunksAndCachesConfirmedMissing_WhenFiftyOneIds()
    {
        // Arrange
        var upstreamRequests = 0;
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test",
            UpstreamRequestObserver = () => upstreamRequests++
        };
        var batches = new List<IReadOnlyCollection<string>>();
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .Returns((
                IReadOnlyCollection<string> ids,
                RequestContext requestContext,
                CancellationToken _) =>
            {
                requestContext.UpstreamRequestObserver?.Invoke();
                batches.Add([.. ids]);
                return Task.FromResult<IReadOnlyDictionary<string, EntityDto>>(
                    new Dictionary<string, EntityDto>());
            });
        var ids = Enumerable.Range(1, 51).Select(value => "Q" + value).ToArray();

        var subject = new QueryCoordinator(client.Object, _scopeFactory);

        // Act
        var first = await subject.GetEntitiesAsync(ids, context, default).ConfigureAwait(false);
        var second = await subject.GetEntitiesAsync(ids, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Empty);
            Assert.That(second, Is.Empty);
            Assert.That(upstreamRequests, Is.EqualTo(2));
            Assert.That(batches, Has.Count.EqualTo(2));
            Assert.That(batches[0], Has.Count.EqualTo(50));
            Assert.That(batches[1], Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task GetEntitiesAsync_KeepsEarlierCompletedEntries_WhenLaterBatchFails()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var calls = 0;
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls++;
                return calls == 1
                    ? Task.FromResult<IReadOnlyDictionary<string, EntityDto>>(
                        new Dictionary<string, EntityDto>())
                    : Task.FromException<IReadOnlyDictionary<string, EntityDto>>(
                        new HttpRequestException("Wikidata rejected the batch."));
            });
        var ids = Enumerable.Range(1, 51).Select(value => "Q" + value).ToArray();

        var subject = new QueryCoordinator(client.Object, _scopeFactory);

        // Act
        var failure = Assert.CatchAsync<HttpRequestException>(async () =>
            await subject.GetEntitiesAsync(ids, context, default).ConfigureAwait(false));
        var cached = await subject.GetEntitiesAsync(["Q1"], context, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure, Is.TypeOf<HttpRequestException>());
            Assert.That(cached, Is.Empty);
            Assert.That(calls, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetEntitiesAsync_FillsSeparateCacheEntries_WhenDifferentLanguageScopes()
    {
        // Arrange
        var calls = 0;
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls++;
                return Task.FromResult<IReadOnlyDictionary<string, EntityDto>>(
                    new Dictionary<string, EntityDto>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Q1"] = new EntityDto { Id = "Q1" }
                    });
            });

        var subject = new QueryCoordinator(client.Object, _scopeFactory);

        // Act
        await subject.GetEntitiesAsync(
            ["Q1"],
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);
        await subject.GetEntitiesAsync(
            ["Q1"],
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en", "fr"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public async Task GetEntitiesAsync_ReturnsPositiveCacheEntry_WhenRequestedQidRedirects()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var calls = 0;
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls++;
                return Task.FromResult<IReadOnlyDictionary<string, EntityDto>>(
                    new Dictionary<string, EntityDto>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Q59431323"] = new EntityDto { Id = "Q42" }
                    });
            });

        var subject = new QueryCoordinator(client.Object, _scopeFactory);

        // Act
        var first = await subject.GetEntitiesAsync(["Q59431323"], context, default)
            .ConfigureAwait(false);
        var second = await subject.GetEntitiesAsync(["Q59431323"], context, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first["Q59431323"].Id, Is.EqualTo("Q42"));
            Assert.That(second["Q59431323"].Id, Is.EqualTo("Q42"));
            Assert.That(calls, Is.EqualTo(1));
        }
    }
}
