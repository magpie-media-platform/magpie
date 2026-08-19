using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata;
using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class QueryCoordinatorTests
{
    [Test]
    public async Task GetIdentityAsync_ConcurrentMisses_Coalesce()
    {
        // Arrange
        IQueryCoordinator subject = new QueryCoordinator(Mock.Of<IWikidataClient>());
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
    public async Task GetIdentityAsync_CancelledWaiter_DoesNotCancelSharedWork()
    {
        // Arrange
        IQueryCoordinator subject = new QueryCoordinator(Mock.Of<IWikidataClient>());
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
    public void GetSearchAsync_AlreadyCancelledOwner_DoesNotLaunchFill()
    {
        // Arrange
        IQueryCoordinator subject = new QueryCoordinator(Mock.Of<IWikidataClient>());
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
    public async Task GetSearchAsync_CancelledOnlyOwner_CancelsFillAndDoesNotCacheCompletion()
    {
        // Arrange
        IQueryCoordinator subject = new QueryCoordinator(Mock.Of<IWikidataClient>());
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
    public async Task GetSearchAsync_OwnerAndJoiner_CancelsFillOnlyAfterBothCancel()
    {
        // Arrange
        IQueryCoordinator subject = new QueryCoordinator(Mock.Of<IWikidataClient>());
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
    public void GetSearchAsync_Failure_IsNotCached()
    {
        // Arrange
        IQueryCoordinator subject = new QueryCoordinator(Mock.Of<IWikidataClient>());
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
        IQueryCoordinator subject = new QueryCoordinator(Mock.Of<IWikidataClient>());
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
        IQueryCoordinator subject = new QueryCoordinator(Mock.Of<IWikidataClient>());
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
        IQueryCoordinator subject = new QueryCoordinator(Mock.Of<IWikidataClient>());
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

    [Test]
    public async Task GetEntitiesAsync_FiftyOneIds_ChunksAndCachesConfirmedMissing()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var batches = new List<IReadOnlyCollection<string>>();
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .Returns((
                IReadOnlyCollection<string> ids,
                RequestContext _,
                CancellationToken _) =>
            {
                batches.Add([.. ids]);
                return Task.FromResult<IReadOnlyDictionary<string, EntityDto>>(
                    new Dictionary<string, EntityDto>());
            });
        var ids = Enumerable.Range(1, 51).Select(value => "Q" + value).ToArray();

        IQueryCoordinator subject = new QueryCoordinator(client.Object);

        // Act
        var first = await subject.GetEntitiesAsync(ids, context, default).ConfigureAwait(false);
        var second = await subject.GetEntitiesAsync(ids, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Empty);
            Assert.That(second, Is.Empty);
            Assert.That(batches, Has.Count.EqualTo(2));
            Assert.That(batches[0], Has.Count.EqualTo(50));
            Assert.That(batches[1], Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task GetEntitiesAsync_LaterBatchFails_KeepsEarlierCompletedEntries()
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

        IQueryCoordinator subject = new QueryCoordinator(client.Object);

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
    public async Task GetEntitiesAsync_DifferentLanguageScopes_FillsSeparateCacheEntries()
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

        IQueryCoordinator subject = new QueryCoordinator(client.Object);

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

        IQueryCoordinator subject = new QueryCoordinator(client.Object);

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
