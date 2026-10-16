using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz;
using Magpie.Extensions.MusicBrainz.Mapping;
using Magpie.Extensions.MusicBrainz.QueryEngine;
using Magpie.Sdk.Caching;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.MusicBrainz;

[TestFixture]
public class MusicBrainzQueryCoordinatorTests
{
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
                cache.Setup(value => value.TryGet(It.IsAny<string>(), out It.Ref<bool>.IsAny))
                    .Returns(new InvocationFunc(invocation =>
                    {
                        var found = values.TryGetValue((string)invocation.Arguments[0], out var value);
                        ((object?[])invocation.Arguments)[1] = value ?? false;
                        return found;
                    }));
                cache.Setup(value => value.Remove(It.IsAny<string>())).Returns<string>(values.Remove);
                return caches[name] = cache.Object;
            });
        var provider = new Mock<IServiceProvider>();
        provider.Setup(value => value.GetService(typeof(ICacheFactory))).Returns(cacheFactory.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(value => value.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(value => value.CreateScope()).Returns(scope.Object);
        _scopeFactory = scopeFactory.Object;
    }

    [Test]
    public async Task GetSearchPageAsync_SharedValue_WhenConcurrentMissesUseSameKey()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory);
        var key = new SearchCacheKey
        {
            Target = QueryTarget.Artist,
            Fingerprint = "same",
            Limit = 25,
            Offset = 0
        };
        var completion = new TaskCompletionSource<FetchResult<SearchPage>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        // Act
        var first = cache.GetSearchPageAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return completion.Task;
            },
            default);
        var second = cache.GetSearchPageAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return completion.Task;
            },
            default);
        completion.SetResult(new FetchResult<SearchPage>
        {
            Outcome = FetchOutcome.Success,
            Value = new SearchPage { Count = 1 }
        });
        await Task.WhenAll(first, second).ConfigureAwait(false);
        var warm = await cache.GetSearchPageAsync(
            key,
            _ => throw new AssertionException("warm cache invoked loader"),
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(warm.Value!.Count, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetSearchPageAsync_Retry_WhenLoaderFails()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory);
        var key = new SearchCacheKey
        {
            Target = QueryTarget.Artist,
            Fingerprint = "failure",
            Limit = 25,
            Offset = 0
        };
        var calls = 0;

        // Act
        var failed = await cache.GetSearchPageAsync(
            key,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<SearchPage>
                {
                    Outcome = FetchOutcome.Unavailable
                });
            },
            default).ConfigureAwait(false);
        var retried = await cache.GetSearchPageAsync(
            key,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<SearchPage>
                {
                    Outcome = FetchOutcome.Success,
                    Value = new SearchPage()
                });
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Outcome, Is.EqualTo(FetchOutcome.Unavailable));
            Assert.That(retried.Outcome, Is.EqualTo(FetchOutcome.Success));
            Assert.That(calls, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetSearchPageAsync_SharedLoad_WhenOneWaiterIsCanceled()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory);
        var key = new SearchCacheKey
        {
            Target = QueryTarget.Track,
            Fingerprint = "cancellation",
            Limit = 25,
            Offset = 0
        };
        var completion = new TaskCompletionSource<FetchResult<SearchPage>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var loaderToken = new CancellationToken(canceled: true);
        using var cancellation = new CancellationTokenSource();

        // Act
        var cancelledWaiter = cache.GetSearchPageAsync(
            key,
            token =>
            {
                loaderToken = token;
                return completion.Task;
            },
            cancellation.Token);
        var survivingWaiter = cache.GetSearchPageAsync(key, _ => completion.Task, default);
        await cancellation.CancelAsync().ConfigureAwait(false);

        // Assert
        Assert.ThrowsAsync<OperationCanceledException>(async () => await cancelledWaiter.ConfigureAwait(false));
        completion.SetResult(new FetchResult<SearchPage>
        {
            Outcome = FetchOutcome.Success,
            Value = new SearchPage { Count = 2 }
        });

        var result = await survivingWaiter.ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaderToken, Is.EqualTo(CancellationToken.None));
            Assert.That(result.Value!.Count, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetEntityAsync_ServesTheSearchProfile_WhenHydrationWasCachedFirst()
    {
        // Arrange
        const string mbid = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3";
        var cache = new QueryCoordinator(_scopeFactory);
        var calls = 0;
        var hydrationKey = new EntityCacheKey
        {
            Kind = EntityKind.Artist,
            Mbid = mbid,
            Profile = LookupProfile.Hydration
        };
        var searchKey = hydrationKey with { Profile = LookupProfile.Search };
        var relationshipSearchKey = searchKey with { Capabilities = LookupCapability.ArtistRelations };

        // Act
        await cache.GetEntityAsync(
            hydrationKey,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<EntityResponseDto>
                {
                    Outcome = FetchOutcome.Success,
                    Value = new EntityResponseDto
                    {
                        Artist = new ArtistDto { Id = mbid, Name = "Queen" }
                    }
                });
            },
            default).ConfigureAwait(false);
        await cache.GetEntityAsync(
            searchKey,
            _ => throw new AssertionException("hydration did not satisfy search"),
            default).ConfigureAwait(false);
        await cache.GetEntityAsync(
            relationshipSearchKey,
            _ => throw new AssertionException("hydration did not satisfy artist-relation search"),
            default).ConfigureAwait(false);

        // Assert
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task GetEntityAsync_LoadsHydrationAgain_WhenTheSearchProfileWasCachedFirst()
    {
        // Arrange
        const string mbid = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3";
        var cache = new QueryCoordinator(_scopeFactory);
        var calls = 0;
        var hydrationKey = new EntityCacheKey
        {
            Kind = EntityKind.Artist,
            Mbid = mbid,
            Profile = LookupProfile.Hydration
        };
        var searchKey = hydrationKey with { Profile = LookupProfile.Search };

        // Act
        await cache.GetEntityAsync(
            searchKey,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<EntityResponseDto>
                {
                    Outcome = FetchOutcome.Success,
                    Value = new EntityResponseDto
                    {
                        Artist = new ArtistDto { Id = mbid, Name = "Queen" }
                    }
                });
            },
            default).ConfigureAwait(false);
        await cache.GetEntityAsync(
            hydrationKey,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<EntityResponseDto>
                {
                    Outcome = FetchOutcome.Success,
                    Value = new EntityResponseDto
                    {
                        Artist = new ArtistDto { Id = mbid, Name = "Queen" }
                    }
                });
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public async Task GetEntityAsync_Result_WhenLabelHydrationSatisfiesUrlRelationSearch()
    {
        // Arrange
        const string mbid = "44444444-4444-4444-8444-444444444444";
        var cache = new QueryCoordinator(_scopeFactory);
        var calls = 0;
        var hydrationKey = new EntityCacheKey
        {
            Kind = EntityKind.Label,
            Mbid = mbid,
            Profile = LookupProfile.Hydration
        };
        var searchKey = hydrationKey with
        {
            Profile = LookupProfile.Search,
            Capabilities = LookupCapability.UrlRelations
        };

        // Act
        await cache.GetEntityAsync(
            hydrationKey,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<EntityResponseDto>
                {
                    Outcome = FetchOutcome.Success,
                    Value = new EntityResponseDto
                    {
                        Label = new LabelDto { Id = mbid, Name = "Blue Note" }
                    }
                });
            },
            default).ConfigureAwait(false);
        await cache.GetEntityAsync(
            searchKey,
            _ => throw new AssertionException("label hydration did not satisfy URL-relation search"),
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                hydrationKey.EffectiveCapabilities & LookupCapability.UrlRelations,
                Is.Not.EqualTo(LookupCapability.None));
            Assert.That(calls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetEntityAsync_ServesTheLeanCapabilities_WhenTheRichOnesWereCachedFirst()
    {
        // Arrange
        const string mbid = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3";
        var cache = new QueryCoordinator(_scopeFactory);
        var calls = 0;
        var richKey = new EntityCacheKey
        {
            Kind = EntityKind.Artist,
            Mbid = mbid,
            Profile = LookupProfile.Search,
            Capabilities = LookupCapability.Aliases | LookupCapability.UrlRelations
        };
        var leanKey = richKey with { Capabilities = LookupCapability.Aliases };

        // Act
        await cache.GetEntityAsync(
            richKey,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<EntityResponseDto>
                {
                    Outcome = FetchOutcome.Success,
                    Value = new EntityResponseDto
                    {
                        Artist = new ArtistDto { Id = mbid, Name = "Queen" }
                    }
                });
            },
            default).ConfigureAwait(false);
        await cache.GetEntityAsync(
            leanKey,
            _ => throw new AssertionException("rich entity did not satisfy lean request"),
            default).ConfigureAwait(false);

        // Assert
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task GetEntityAsync_LoadsTheRichCapabilitiesAgain_WhenTheLeanOnesWereCachedFirst()
    {
        // Arrange
        const string mbid = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3";
        var cache = new QueryCoordinator(_scopeFactory);
        var calls = 0;
        var richKey = new EntityCacheKey
        {
            Kind = EntityKind.Artist,
            Mbid = mbid,
            Profile = LookupProfile.Search,
            Capabilities = LookupCapability.Aliases | LookupCapability.UrlRelations
        };
        var leanKey = richKey with { Capabilities = LookupCapability.Aliases };

        // Act
        await cache.GetEntityAsync(
            leanKey,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<EntityResponseDto>
                {
                    Outcome = FetchOutcome.Success,
                    Value = new EntityResponseDto
                    {
                        Artist = new ArtistDto { Id = mbid, Name = "Queen" }
                    }
                });
            },
            default).ConfigureAwait(false);
        await cache.GetEntityAsync(
            richKey,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<EntityResponseDto>
                {
                    Outcome = FetchOutcome.Success,
                    Value = new EntityResponseDto
                    {
                        Artist = new ArtistDto { Id = mbid, Name = "Queen" }
                    }
                });
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public async Task GetEntityAsync_Result_WhenConcurrentRichRequestsMatch()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory);
        var key = new EntityCacheKey
        {
            Kind = EntityKind.Recording,
            Mbid = "550e8400-e29b-41d4-a716-446655440000",
            Profile = LookupProfile.Search,
            Capabilities = LookupCapability.Aliases | LookupCapability.UrlRelations
        };
        var completion = new TaskCompletionSource<FetchResult<EntityResponseDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var first = cache.GetEntityAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return completion.Task;
            },
            default);
        var second = cache.GetEntityAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return completion.Task;
            },
            default);
        completion.SetResult(new FetchResult<EntityResponseDto>
        {
            Outcome = FetchOutcome.Success,
            Value = new EntityResponseDto
            {
                Recording = new RecordingDto { Id = key.Mbid, Title = "Bohemian Rhapsody" }
            }
        });

        // Act
        await Task.WhenAll(first, second).ConfigureAwait(false);

        // Assert
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task GetEntityAsync_Result_WhenConcurrentRichRequestSatisfiesLeanWaiter()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory);
        var richKey = new EntityCacheKey
        {
            Kind = EntityKind.Artist,
            Mbid = "550e8400-e29b-41d4-a716-446655440000",
            Profile = LookupProfile.Search,
            Capabilities = LookupCapability.Aliases | LookupCapability.UrlRelations
        };
        var leanKey = richKey with { Capabilities = LookupCapability.Aliases };
        var completion = new TaskCompletionSource<FetchResult<EntityResponseDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        // Act
        var rich = cache.GetEntityAsync(
            richKey,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return completion.Task;
            },
            default);
        var lean = cache.GetEntityAsync(
            leanKey,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return completion.Task;
            },
            default);
        completion.SetResult(new FetchResult<EntityResponseDto>
        {
            Outcome = FetchOutcome.Success,
            Value = new EntityResponseDto
            {
                Artist = new ArtistDto { Id = richKey.Mbid, Name = "Queen" }
            }
        });
        await Task.WhenAll(rich, lean).ConfigureAwait(false);

        // Assert
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task GetEntityAsync_Result_WhenIdentityWasNotFound()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory);
        var key = new EntityCacheKey
        {
            Kind = EntityKind.ReleaseGroup,
            Mbid = "550e8400-e29b-41d4-a716-446655440000",
            Profile = LookupProfile.Search,
            Capabilities = LookupCapability.Aliases
        };
        var calls = 0;

        // Act
        var first = await cache.GetEntityAsync(
            key,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<EntityResponseDto>
                {
                    Outcome = FetchOutcome.NotFound
                });
            },
            default).ConfigureAwait(false);
        var cached = await cache.GetEntityAsync(
            key with
            {
                Profile = LookupProfile.Hydration,
                Capabilities = LookupCapability.UrlRelations
            },
            _ => throw new AssertionException("identity-wide not-found invoked loader"),
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Outcome, Is.EqualTo(FetchOutcome.NotFound));
            Assert.That(cached.Outcome, Is.EqualTo(FetchOutcome.NotFound));
            Assert.That(calls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetIsrcAsync_ReturnsCachedNotFound_WhenLookupRepeats()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory);
        var calls = 0;

        // Act
        var first = await cache.GetIsrcAsync(
            "USRC17607839",
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<SearchPage> { Outcome = FetchOutcome.NotFound });
            },
            default).ConfigureAwait(false);
        var cached = await cache.GetIsrcAsync(
            "USRC17607839",
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<SearchPage> { Outcome = FetchOutcome.NotFound });
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Outcome, Is.EqualTo(FetchOutcome.NotFound));
            Assert.That(cached.Outcome, Is.EqualTo(FetchOutcome.NotFound));
            Assert.That(calls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetTrackReleasesAsync_ServesTheCachedBrowse_WhenTheTrackRepeats()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory);
        var calls = 0;

        // Act
        var first = await cache.GetTrackReleasesAsync(
            "63a9abaa-7b6c-4290-83e7-828de5802e7e",
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<ReleaseSearchResponseDto>
                {
                    Outcome = FetchOutcome.Success,
                    Value = new ReleaseSearchResponseDto
                    {
                        Releases = [new ReleaseDto { Id = "86ecd18e-f1fb-4ac7-b77a-5d0fd4bd830e" }]
                    }
                });
            },
            default).ConfigureAwait(false);
        var cached = await cache.GetTrackReleasesAsync(
            "63a9abaa-7b6c-4290-83e7-828de5802e7e",
            _ => throw new AssertionException("warm cache invoked loader"),
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Outcome, Is.EqualTo(FetchOutcome.Success));
            Assert.That(cached.Value!.Releases![0].Id, Is.EqualTo("86ecd18e-f1fb-4ac7-b77a-5d0fd4bd830e"));
            Assert.That(calls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetCoverAsync_Result_WhenMissingCoverLookupRepeats()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory);
        var key = new CoverCacheKey
        {
            ReleaseGroupMbid = "550e8400-e29b-41d4-a716-446655440000",
            ThumbnailSize = 250
        };
        var calls = 0;

        // Act
        var first = await cache.GetCoverAsync(
            key,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<Uri> { Outcome = FetchOutcome.NotFound });
            },
            default).ConfigureAwait(false);
        var cached = await cache.GetCoverAsync(
            key,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<Uri> { Outcome = FetchOutcome.NotFound });
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Outcome, Is.EqualTo(FetchOutcome.NotFound));
            Assert.That(cached.Outcome, Is.EqualTo(FetchOutcome.NotFound));
            Assert.That(calls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetArtistBrowsePageAsync_Result_WhenConcurrentRequestsMatch()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory);
        var key = new ArtistBrowseCacheKey
        {
            RelatedEntity = ArtistCompositionEntity.Work,
            RelatedMbid = "11111111-1111-4111-8111-111111111111",
            Limit = 100,
            Offset = 0
        };
        var completion = new TaskCompletionSource<FetchResult<ArtistBrowseResponseDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        // Act
        var first = cache.GetArtistBrowsePageAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return completion.Task;
            },
            default);
        var second = cache.GetArtistBrowsePageAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return completion.Task;
            },
            default);
        completion.SetResult(new FetchResult<ArtistBrowseResponseDto>
        {
            Outcome = FetchOutcome.Success,
            Value = new ArtistBrowseResponseDto { ArtistCount = 1 }
        });
        await Task.WhenAll(first, second).ConfigureAwait(false);
        var warm = await cache.GetArtistBrowsePageAsync(
            key,
            _ => throw new AssertionException("warm artist browse invoked loader"),
            default).ConfigureAwait(false);
        var enrichedKey = key with { Capabilities = LookupCapability.Aliases };
        var enriched = await cache.GetArtistBrowsePageAsync(
            enrichedKey,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return completion.Task;
            },
            default).ConfigureAwait(false);
        var enrichedWarm = await cache.GetArtistBrowsePageAsync(
            enrichedKey,
            _ => throw new AssertionException("warm enriched artist browse invoked loader"),
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(warm.Value!.ArtistCount, Is.EqualTo(1));
            Assert.That(enriched.Value!.ArtistCount, Is.EqualTo(1));
            Assert.That(enrichedWarm.Value!.ArtistCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetVerifiedScanAsync_PreservesAnsweredEmptyScan_WhenSharedNextRequestFails()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory);
        var scan = new VerifiedScan();
        scan.AppendAccepted([], 100, sourceExhausted: false, truncated: false);
        await cache.GetVerifiedScanAsync(
            "answered-empty",
            _ => true,
            (_, _) => Task.FromResult(new FetchResult<VerifiedScan>
            {
                Outcome = FetchOutcome.Success,
                Value = scan
            }),
            default).ConfigureAwait(false);
        var completion = new TaskCompletionSource<FetchResult<VerifiedScan>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        // Act
        var first = cache.GetVerifiedScanAsync(
            "answered-empty",
            _ => false,
            (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return completion.Task;
            },
            default);
        var second = cache.GetVerifiedScanAsync(
            "answered-empty",
            _ => false,
            (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return completion.Task;
            },
            default);
        completion.SetResult(new FetchResult<VerifiedScan> { Outcome = FetchOutcome.Unavailable });
        var results = await Task.WhenAll(first, second).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(results.All(result => result.Outcome == FetchOutcome.Unavailable), Is.True);
            Assert.That(results.All(result => ReferenceEquals(result.Value, scan)), Is.True);
            Assert.That(scan.Read().HitCount, Is.Zero);
        }
    }

    [Test]
    public async Task GetArtistResolutionAsync_Result_WhenFailureThenSuccess()
    {
        // Arrange
        var cache = new QueryCoordinator(_scopeFactory);
        var key = new ArtistResolutionCacheKey
        {
            Entity = ArtistCompositionEntity.ReleaseGroup,
            Name = "Kind of Blue"
        };
        var calls = 0;

        // Act
        var failed = await cache.GetArtistResolutionAsync(
            key,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<ArtistResolutionResponse>
                {
                    Outcome = FetchOutcome.Unavailable
                });
            },
            default).ConfigureAwait(false);
        var succeeded = await cache.GetArtistResolutionAsync(
            key,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<ArtistResolutionResponse>
                {
                    Outcome = FetchOutcome.Success,
                    Value = new ArtistResolutionResponse { Count = 1 }
                });
            },
            default).ConfigureAwait(false);
        var warm = await cache.GetArtistResolutionAsync(
            key,
            _ => throw new AssertionException("warm artist resolution invoked loader"),
            default).ConfigureAwait(false);
        var identifiedKey = key with { RelatedMbid = "11111111-1111-4111-8111-111111111111" };
        var identified = await cache.GetArtistResolutionAsync(
            identifiedKey,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<ArtistResolutionResponse>
                {
                    Outcome = FetchOutcome.Success,
                    Value = new ArtistResolutionResponse { Count = 1 }
                });
            },
            default).ConfigureAwait(false);
        var identifiedWarm = await cache.GetArtistResolutionAsync(
            identifiedKey,
            _ => throw new AssertionException("warm identified artist resolution invoked loader"),
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Outcome, Is.EqualTo(FetchOutcome.Unavailable));
            Assert.That(succeeded.Outcome, Is.EqualTo(FetchOutcome.Success));
            Assert.That(warm.Outcome, Is.EqualTo(FetchOutcome.Success));
            Assert.That(identified.Outcome, Is.EqualTo(FetchOutcome.Success));
            Assert.That(identifiedWarm.Outcome, Is.EqualTo(FetchOutcome.Success));
            Assert.That(calls, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task GetEntityAsync_ServesTheSearchProfile_WhenHydrationReplacedTheSearchEntry()
    {
        // Arrange
        const string mbid = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3";
        var cache = new QueryCoordinator(_scopeFactory);
        var calls = 0;
        var hydrationKey = new EntityCacheKey
        {
            Kind = EntityKind.Artist,
            Mbid = mbid,
            Profile = LookupProfile.Hydration
        };
        var searchKey = hydrationKey with { Profile = LookupProfile.Search };

        // Act
        await cache.GetEntityAsync(
            searchKey,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<EntityResponseDto>
                {
                    Outcome = FetchOutcome.Success,
                    Value = new EntityResponseDto
                    {
                        Artist = new ArtistDto { Id = mbid, Name = "Queen" }
                    }
                });
            },
            default).ConfigureAwait(false);
        await cache.GetEntityAsync(
            hydrationKey,
            _ =>
            {
                calls++;
                return Task.FromResult(new FetchResult<EntityResponseDto>
                {
                    Outcome = FetchOutcome.Success,
                    Value = new EntityResponseDto
                    {
                        Artist = new ArtistDto { Id = mbid, Name = "Queen" }
                    }
                });
            },
            default).ConfigureAwait(false);
        var warm = await cache.GetEntityAsync(
            searchKey,
            _ => throw new AssertionException("hydration entry did not satisfy the search request"),
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(warm.Value!.Artist!.Name, Is.EqualTo("Queen"));
        }
    }
}
