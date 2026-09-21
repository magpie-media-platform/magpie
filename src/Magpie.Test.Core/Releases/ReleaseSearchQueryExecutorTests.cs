using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Caching;
using Magpie.Core.Config;
using Magpie.Core.Observability;
using Magpie.Core.Releases;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Releases;
using Magpie.Sdk.Caching;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Releases;

[TestFixture]
[NonParallelizable]
public sealed class ReleaseSearchQueryExecutorTests
{
    private Mock<IIndexerRepository> _indexers = null!;
    private Mock<IIndexerConnectionRepository> _indexerConnections = null!;
    private Mock<IEventBus> _eventBus = null!;
    private Mock<IIndexerIntegration> _indexerIntegration = null!;
    private Mock<IReleaseEvaluationService> _releaseEvaluation = null!;
    private ReleaseSearchQueryExecutor _subject = null!;
    private List<ReleaseSearchRequest> _issuedRequests = null!;
    private Guid _indexerId;
    private Guid _connectionId;

    [SetUp]
    public void SetUp()
    {
        _indexerId = Guid.NewGuid();
        _connectionId = Guid.NewGuid();
        _issuedRequests = [];

        _indexers = new Mock<IIndexerRepository> { CallBase = true };
        _indexers
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Indexer
                {
                    Id = _indexerId,
                    ProviderType = "test-indexer",
                    Name = "Test Indexer",
                    CreatedAt = DateTime.UtcNow,
                },
            ]);

        _indexerConnections = new Mock<IIndexerConnectionRepository> { CallBase = true };
        _indexerConnections
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new IndexerConnection
                {
                    Id = _connectionId,
                    IndexerId = _indexerId,
                    Name = "Test Connection",
                    Enabled = true,
                    CreatedAt = DateTime.UtcNow,
                },
            ]);

        _eventBus = new Mock<IEventBus>();
        _indexerConnections
            .Setup(repository => repository.PatchAsync(
                It.IsAny<IndexerConnectionPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _indexerIntegration = new Mock<IIndexerIntegration>();
        _indexerIntegration
            .SetupGet(integration => integration.ProviderType)
            .Returns("test-indexer");
        _indexerIntegration
            .SetupGet(integration => integration.SupportedContentTypes)
            .Returns([]);
        _indexerIntegration
            .SetupGet(integration => integration.SearchFacets)
            .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "q", "imdbid", "season", "episode" },]);
        _indexerIntegration
            .SetupGet(integration => integration.SupportsKeywordSearch)
            .Returns(true);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                _issuedRequests.Add(request))
            .ReturnsAsync([]);

        _releaseEvaluation = new Mock<IReleaseEvaluationService>();
        _releaseEvaluation
            .Setup(service => service.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()))
            .Returns(new ReleaseAssessment { MatchResult = ReleaseMatchResult.Confident });
        _releaseEvaluation
            .Setup(service => service.EffectiveQualityRank(
                It.IsAny<ReleaseAssessment>(),
                It.IsAny<ReleaseEvaluationContext>()))
            .Returns((ReleaseAssessment assessment, ReleaseEvaluationContext _) =>
                assessment.MatchedRankOrdinal ?? int.MaxValue);

        _subject = new ReleaseSearchQueryExecutor(
            _indexers.Object,
            _indexerConnections.Object,
            _eventBus.Object,
            [_indexerIntegration.Object],
            [],
            TimeProvider.System,
            NullLogger<ReleaseSearchQueryExecutor>.Instance,
            _releaseEvaluation.Object,
            cacheFactory: Mock.Of<ICacheFactory>(),
            releaseComparers: [],
            searchOptions: new SearchOptions());
    }

    [Test]
    public async Task ExecuteAsync_ReturnsProducingPhaseFacts_WhenReleasesAreReturned()
    {
        // Arrange
        var phaseOneFacts = new[]
        {
            new ContentFact { Value = "Show Title" },
        };
        var phaseTwoFacts = new[]
        {
            new ContentFact
            {
                Facets = new Dictionary<string, string> { ["imdbid"] = "tt123" },
            },
        };
        var phases = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Show Title" },
                ReleaseFacts = phaseOneFacts,
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    IdSearchParameter = "imdbid",
                    IdSearchValue = "tt123",
                },
                ReleaseFacts = phaseTwoFacts,
            },
        };
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            (ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = request.Title ?? request.IdSearchValue ?? "release",
                    ExternalId = request.Title is null ? "id-release" : "title-release",
                    PublishDate = DateTime.UtcNow,
                },
            ]);

        // Act
        var result = await _subject.ExecuteAsync(phases, [], new HashSet<Guid> { _indexerId }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].Releases.Single().ExternalId, Is.EqualTo("title-release"));
            Assert.That(result[0].ReleaseFacts, Is.SameAs(phaseOneFacts));
            Assert.That(result[1].Releases.Single().ExternalId, Is.EqualTo("id-release"));
            Assert.That(result[1].ReleaseFacts, Is.SameAs(phaseTwoFacts));
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsConnection_WhenIntegrationDoesNotSupportRequestFacets()
    {
        // Arrange
        _indexerIntegration
            .SetupGet(integration => integration.SearchFacets)
            .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "q", "genre" },]);
        var phases = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    IdSearchParameter = "imdbid",
                    IdSearchValue = "tt123",
                },
                ReleaseFacts = [new ContentFact { Facets = new Dictionary<string, string> { ["imdbid"] = "tt123" }, },],
            },
        };

        // Act
        var result = await _subject.ExecuteAsync(phases, [], new HashSet<Guid> { _indexerId }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_issuedRequests, Is.Empty);
            Assert.That(result[0].ConnectionsAttempted, Is.Zero);
            Assert.That(result[0].ConnectionsSucceeded, Is.Zero);
            _indexerIntegration.Verify(
                integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsConnection_WhenTheFacetSetOmitsTheKeywordSpelling()
    {
        // Arrange
        _indexerIntegration
            .SetupGet(integration => integration.SearchFacets)
            .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "season" },]);
        var phases = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    Title = "pilot",
                    Criteria = new Dictionary<string, IReadOnlyList<string>> { ["season"] = ["1"] },
                },
                ReleaseFacts = [new ContentFact { Value = "pilot" }],
            },
        };

        // Act
        var result = await _subject.ExecuteAsync(phases, [], new HashSet<Guid> { _indexerId }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result[0].ConnectionsAttempted, Is.Zero);
            _indexerIntegration.Verify(
                integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsConnection_WhenStatusIsUnhealthy()
    {
        // Arrange
        _indexerConnections
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new IndexerConnection
                {
                    Id = _connectionId,
                    IndexerId = _indexerId,
                    Name = "Unhealthy Connection",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Unhealthy,
                    CreatedAt = DateTime.UtcNow,
                },
            ]);
        var phases = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "pilot" },
                ReleaseFacts = [new ContentFact { Value = "pilot" }],
            },
        };

        // Act
        var result = await _subject.ExecuteAsync(phases, [], new HashSet<Guid> { _indexerId }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_issuedRequests, Is.Empty);
            Assert.That(result[0].ConnectionsAttempted, Is.Zero);
            Assert.That(result[0].ConnectionsSucceeded, Is.Zero);
        }
    }

    [TestCase(IntegrationHealthStatus.Unknown)]
    [TestCase(IntegrationHealthStatus.Healthy)]
    [TestCase(IntegrationHealthStatus.Stale)]
    [TestCase(IntegrationHealthStatus.Degraded)]
    [TestCase(IntegrationHealthStatus.Stabilizing)]
    public async Task ExecuteAsync_RunsConnection_WhenStatusIsNotUnhealthy(IntegrationHealthStatus status)
    {
        // Arrange
        _indexerConnections
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new IndexerConnection
                {
                    Id = _connectionId,
                    IndexerId = _indexerId,
                    Name = "Eligible Connection",
                    Enabled = true,
                    Status = status,
                    CreatedAt = DateTime.UtcNow,
                },
            ]);
        var phases = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "pilot" },
                ReleaseFacts = [new ContentFact { Value = "pilot" }],
            },
        };

        // Act
        var result = await _subject.ExecuteAsync(phases, [], new HashSet<Guid> { _indexerId }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_issuedRequests, Has.Count.EqualTo(1));
            Assert.That(result[0].ConnectionsAttempted, Is.EqualTo(1));
            Assert.That(result[0].ConnectionsSucceeded, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ExecuteAsync_RunsEveryPhase_WhenQueryContainsMultiplePhases()
    {
        // Arrange
        var phases = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    IdSearchParameter = "imdbid",
                    IdSearchValue = "tt1160419",
                },
                ReleaseFacts =
                [new ContentFact { Facets = new Dictionary<string, string> { ["imdbid"] = "tt1160419" }, },],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "pilot" },
                ReleaseFacts = [new ContentFact { Value = "pilot" }],
            },
        };

        // Act
        var result = await _subject.ExecuteAsync(phases, [], new HashSet<Guid> { _indexerId }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(_issuedRequests.Any(request => request.IdSearchParameter == "imdbid"), Is.True);
            Assert.That(_issuedRequests.Any(request => request.Title == "pilot"), Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_CountsConnectionAttemptedAndSucceeded_WhenConnectionAnswers()
    {
        // Arrange
        var phases = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "pilot" },
                ReleaseFacts = [new ContentFact { Value = "pilot" }],
            },
        };

        // Act
        var result = await _subject.ExecuteAsync(phases, [], new HashSet<Guid> { _indexerId }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result[0].ConnectionsAttempted, Is.EqualTo(1));
            Assert.That(result[0].ConnectionsSucceeded, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ExecuteAsync_CountsConnectionAttemptedButNotSucceeded_WhenConnectionErrors()
    {
        // Arrange
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("indexer unreachable"));
        var phases = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "pilot" },
                ReleaseFacts = [new ContentFact { Value = "pilot" }],
            },
        };

        // Act
        var result = await _subject.ExecuteAsync(phases, [], new HashSet<Guid> { _indexerId }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result[0].ConnectionsAttempted, Is.EqualTo(1));
            Assert.That(result[0].ConnectionsSucceeded, Is.Zero);
        }
    }

    [Test]
    public async Task ExecuteAsync_ReturnsOtherIndexerReleases_WhenThreeConcurrentSearchesTimeOut()
    {
        // Arrange
        var healthyIndexerId = Guid.NewGuid();
        var healthyConnectionId = Guid.NewGuid();
        var timedOutSearches = 0;
        var allSearchesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _indexers
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Indexer
                {
                    Id = _indexerId,
                    ProviderType = "test-indexer",
                    Name = "Rate Limited Indexer",
                    CreatedAt = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc),
                },
                new Indexer
                {
                    Id = healthyIndexerId,
                    ProviderType = "healthy-indexer",
                    Name = "Healthy Indexer",
                    CreatedAt = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc),
                },
            ]);
        _indexerConnections
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new IndexerConnection
                {
                    Id = _connectionId,
                    IndexerId = _indexerId,
                    Name = "Rate Limited Connection",
                    Enabled = true,
                    CreatedAt = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc),
                },
                new IndexerConnection
                {
                    Id = healthyConnectionId,
                    IndexerId = healthyIndexerId,
                    Name = "Healthy Connection",
                    Enabled = true,
                    CreatedAt = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc),
                },
            ]);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (Interlocked.Increment(ref timedOutSearches) == 3)
                {
                    allSearchesStarted.TrySetResult();
                }

                await allSearchesStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                throw new TaskCanceledException(
                    "The request was canceled due to the configured HttpClient.Timeout elapsing.");
            });
        var healthyIndexerIntegration = new Mock<IIndexerIntegration>();
        healthyIndexerIntegration
            .SetupGet(integration => integration.ProviderType)
            .Returns("healthy-indexer");
        healthyIndexerIntegration
            .SetupGet(integration => integration.SearchFacets)
            .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "q" },]);
        healthyIndexerIntegration
            .SetupGet(integration => integration.SupportsKeywordSearch)
            .Returns(true);
        healthyIndexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Healthy Result",
                    ExternalId = "healthy-release",
                    PublishDate = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc),
                },
            ]);
        _subject = new ReleaseSearchQueryExecutor(
            _indexers.Object,
            _indexerConnections.Object,
            _eventBus.Object,
            [_indexerIntegration.Object, healthyIndexerIntegration.Object],
            [],
            TimeProvider.System,
            NullLogger<ReleaseSearchQueryExecutor>.Instance,
            releaseEvaluationService: Mock.Of<IReleaseEvaluationService>(),
            cacheFactory: Mock.Of<ICacheFactory>(),
            releaseComparers: [],
            searchOptions: new SearchOptions());
        var phases = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "pilot" },
                ReleaseFacts = [new ContentFact { Value = "pilot" }],
            },
        };
        var applicableIndexerIds = new HashSet<Guid> { _indexerId, healthyIndexerId };

        // Act
        var results = await Task.WhenAll(
            _subject.ExecuteAsync(phases, [], applicableIndexerIds),
            _subject.ExecuteAsync(phases, [], applicableIndexerIds),
            _subject.ExecuteAsync(phases, [], applicableIndexerIds)).ConfigureAwait(false);

        // Assert
        var executedPhases = results.SelectMany(result => result).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results, Has.Length.EqualTo(3));
            Assert.That(timedOutSearches, Is.EqualTo(3));
            Assert.That(
                executedPhases.Select(phase => phase.ConnectionsAttempted),
                Is.EqualTo([2, 2, 2]));
            Assert.That(
                executedPhases.Select(phase => phase.ConnectionsSucceeded),
                Is.EqualTo([1, 1, 1]));
            Assert.That(
                executedPhases.SelectMany(phase => phase.Releases).Select(release => release.ExternalId),
                Is.EqualTo(["healthy-release", "healthy-release", "healthy-release"]));
        }
    }

    [Test]
    public async Task ExecuteAsync_MergesExternalIds_WhenDuplicateExternalIdAppearsInOneRequest()
    {
        // Arrange
        var first = new Release
        {
            Protocol = "torrent",
            Title = "Dune",
            ExternalId = "release-1",
            PublishDate = DateTime.UtcNow,
        };
        first.ExternalIds.Add(new ExternalId { Source = "imdb", Value = "tt1160419", Ordinal = 0 });
        var duplicate = new Release
        {
            Protocol = "torrent",
            Title = "Dune",
            ExternalId = "release-1",
            PublishDate = DateTime.UtcNow,
        };
        duplicate.ExternalIds.Add(new ExternalId { Source = "tmdb", Value = "438631", Ordinal = 0 });
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, duplicate]);
        var phases = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };

        // Act
        var result = await _subject.ExecuteAsync(phases, [], new HashSet<Guid> { _indexerId }).ConfigureAwait(false);

        // Assert
        var release = result.Single().Releases.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(release.ExternalIds, Has.Count.EqualTo(2));
            Assert.That(
                release.ExternalIds.Select(id => (id.Source, id.Value)),
                Is.EquivalentTo([("imdb", "tt1160419"), ("tmdb", "438631")]));
        }
    }

    [Test]
    public async Task ExecuteAsync_ReturnsSeparateReleases_WhenDuplicateExternalIdHasDifferentImmutableHashes()
    {
        // Arrange
        var first = new Release
        {
            Protocol = "torrent",
            Title = "Dune",
            ExternalId = "release-1",
            PublishDate = DateTime.UtcNow,
            Size = 1_000,
            IdentityKeys =
            {
                new ReleaseIdentityKey { Authority = "fallback", Value = "shared-fallback" },
                new ReleaseIdentityKey
                {
                    Authority = "btih",
                    Value = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                },
            },
        };
        var second = new Release
        {
            Protocol = "torrent",
            Title = "Dune",
            ExternalId = "release-1",
            PublishDate = DateTime.UtcNow,
            Size = 1_000,
            IdentityKeys =
            {
                new ReleaseIdentityKey { Authority = "fallback", Value = "shared-fallback" },
                new ReleaseIdentityKey
                {
                    Authority = "btih",
                    Value = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                },
            },
        };
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        var phases = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };

        // Act
        var result = await _subject.ExecuteAsync(phases, [], new HashSet<Guid> { _indexerId }).ConfigureAwait(false);

        // Assert
        Assert.That(
            result.Single().Releases
                .Select(release => release.IdentityKeys.Single(key => key.Authority == "btih").Value),
            Is.EquivalentTo(
            [
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            ]));
    }

    [Test]
    public async Task ExecuteAsync_CoalescesSameIndexerInfoUrl_WhenExternalIdsAreMissing()
    {
        // Arrange
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Dune first",
                    InfoUrl = new Uri("https://tracker.test/details/1"),
                },
                new Release
                {
                    Protocol = "torrent",
                    Title = "Dune duplicate",
                    InfoUrl = new Uri("https://tracker.test/details/1"),
                },
            ]);
        var phases = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };

        // Act
        var result = await _subject.ExecutePlanAsync(
            phases,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        Assert.That(result.Results, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ExecutePlanAsync_StopsConnection_WhenAnExternalIdResultIsUseful()
    {
        // Arrange
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    IdSearchParameter = "imdbid",
                    IdSearchValue = "tt123",
                },
                ReleaseFacts = [new ContentFact { Facets = new Dictionary<string, string> { ["imdbid"] = "tt123" } }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                _issuedRequests.Add(request))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Dune",
                    ExternalId = "release-1",
                    PublishDate = new DateTime(2026, 1, 1),
                },
            ]);

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results, Has.Count.EqualTo(1));
            Assert.That(_issuedRequests, Has.Count.EqualTo(1));
            Assert.That(_issuedRequests[0].IdSearchValue, Is.EqualTo("tt123"));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_SucceedsWithoutRequestingLaterId_WhenFirstIdIsUseful()
    {
        // Arrange
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    IdSearchParameter = "imdbid",
                    IdSearchValue = "tt123",
                },
                ReleaseFacts = [new ContentFact { Value = "first-id" }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    IdSearchParameter = "imdbid",
                    IdSearchValue = "tt456",
                },
                ReleaseFacts = [new ContentFact { Value = "second-id" }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "keyword" },
                ReleaseFacts = [new ContentFact { Value = "keyword" }],
            },
        };
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Returns((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                request.IdSearchValue == "tt456"
                    ? Task.FromException<IEnumerable<Release>>(new InvalidOperationException("later ID failed"))
                    : Task.FromResult<IEnumerable<Release>>(
                    [
                        new Release
                        {
                            Protocol = "torrent",
                            Title = "identity",
                            ExternalId = "identity",
                        },
                    ]));

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: true,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results, Has.Count.EqualTo(1));
            _indexerIntegration.Verify(integration => integration.SearchAsync(
                It.Is<ReleaseSearchRequest>(request => request.IdSearchValue == "tt123"),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            _indexerIntegration.Verify(integration => integration.SearchAsync(
                It.Is<ReleaseSearchRequest>(request => request.IdSearchValue == "tt456"),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _indexerIntegration.Verify(integration => integration.SearchAsync(
                It.Is<ReleaseSearchRequest>(request => request.Title == "keyword"),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecutePlanAsync_ContinuesToKeyword_WhenManualIdResultIsNotUseful()
    {
        // Arrange
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    IdSearchParameter = "imdbid",
                    IdSearchValue = "tt123",
                },
                ReleaseFacts = [new ContentFact { Value = "id" }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "keyword" },
                ReleaseFacts = [new ContentFact { Value = "keyword" }],
            },
        };
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                _issuedRequests.Add(request))
            .ReturnsAsync((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = request.IdSearchParameter is null ? "keyword" : "id",
                    ExternalId = request.IdSearchParameter is null ? "keyword" : "id",
                },
            ]);
        _releaseEvaluation
            .Setup(service => service.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()))
            .Returns((Release release, IReadOnlyList<ContentFact> _, ReleaseEvaluationContext _) =>
                new ReleaseAssessment
                {
                    MatchResult = release.ExternalId == "keyword"
                        ? ReleaseMatchResult.Confident
                        : ReleaseMatchResult.NoMatch,
                });

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_issuedRequests.Select(request => request.IdSearchParameter is null ? "keyword" : "id"),
                Is.EqualTo(["id", "keyword"]));
            Assert.That(result.Results.Select(value => value.Assessment.MatchResult),
                Is.EquivalentTo([ReleaseMatchResult.NoMatch, ReleaseMatchResult.Confident]));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_DoesNotChargeKeywordDuplicateOfWeakId_WhenAKeywordResultDuplicatesAWeakId()
    {
        // Arrange
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    IdSearchParameter = "imdbid",
                    IdSearchValue = "tt123",
                },
                ReleaseFacts = [new ContentFact { Value = "id" }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "keyword" },
                ReleaseFacts = [new ContentFact { Value = "keyword" }],
            },
        };
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                request.IdSearchParameter is not null
                    ?
                    [
                        new Release
                        {
                            Protocol = "torrent",
                            Title = "A from ID",
                            ExternalId = "a-id",
                            IdentityKeys = { new ReleaseIdentityKey { Authority = "btih", Value = "a" }, },
                        },
                    ]
                    :
                    [
                        new Release
                        {
                            Protocol = "torrent",
                            Title = "A from keyword",
                            ExternalId = "a-keyword",
                            IdentityKeys = { new ReleaseIdentityKey { Authority = "btih", Value = "a" }, },
                        },
                        new Release
                        {
                            Protocol = "torrent",
                            Title = "B",
                            ExternalId = "b",
                            IdentityKeys = { new ReleaseIdentityKey { Authority = "btih", Value = "b" }, },
                        },
                    ]);
        _releaseEvaluation
            .Setup(service => service.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()))
            .Returns((Release release, IReadOnlyList<ContentFact> _, ReleaseEvaluationContext _) =>
                new ReleaseAssessment
                {
                    MatchResult = release.IdentityKeys.Any(key => key.Value == "b")
                        ? ReleaseMatchResult.Confident
                        : ReleaseMatchResult.NoMatch,
                });

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: true,
            maximumResults: 2).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results, Has.Count.EqualTo(1));
            Assert.That(result.Results[0].Release.ExternalId, Is.EqualTo("b"));
            Assert.That(result.Results[0].Assessment.MatchResult, Is.EqualTo(ReleaseMatchResult.Confident));
            _releaseEvaluation.Verify(service => service.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()), Times.Exactly(3));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_ReassessesDuplicateWithoutAnotherSlot_WhenIdentityMatches()
    {
        // Arrange
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    IdSearchParameter = "imdbid",
                    IdSearchValue = "tt123",
                },
                ReleaseFacts = [new ContentFact { Value = "weak" }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    IdSearchParameter = "imdbid",
                    IdSearchValue = "tt456",
                },
                ReleaseFacts = [new ContentFact { Value = "strong" }],
            },
        };
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = request.IdSearchValue!,
                    ExternalId = request.IdSearchValue!,
                    IdentityKeys = { new ReleaseIdentityKey { Authority = "btih", Value = "shared" }, },
                },
            ]);
        _releaseEvaluation
            .Setup(service => service.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()))
            .Returns((Release _, IReadOnlyList<ContentFact> facts, ReleaseEvaluationContext _) =>
                new ReleaseAssessment
                {
                    MatchResult = facts.Any(fact => fact.Value == "strong")
                        ? ReleaseMatchResult.Confident
                        : ReleaseMatchResult.NoMatch,
                });

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: true,
            maximumResults: 1).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results, Has.Count.EqualTo(1));
            Assert.That(result.Results[0].Assessment.MatchResult, Is.EqualTo(ReleaseMatchResult.Confident));
            Assert.That(result.Results[0].ReleaseFacts.Select(fact => fact.Value), Is.EqualTo(["weak", "strong"]));
            Assert.That(result.Results[0].PlanNodeIds,
                Is.EqualTo(["connection-1-request-1", "connection-1-request-2"]));
            _releaseEvaluation.Verify(service => service.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()), Times.Exactly(2));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_ReturnsAllEvaluatedResults_WhenAutomaticFilteringHasNotRun()
    {
        // Arrange
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Dune good",
                    ExternalId = "good",
                    PublishDate = new DateTime(2026, 1, 1),
                },
                new Release
                {
                    Protocol = "torrent",
                    Title = "Dune bad",
                    ExternalId = "bad",
                    PublishDate = new DateTime(2026, 1, 1),
                },
            ]);
        _releaseEvaluation
            .Setup(service => service.AssessMatch(
                It.Is<Release>(release => release.ExternalId == "bad"),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()))
            .Returns(new ReleaseAssessment { MatchResult = ReleaseMatchResult.NoMatch });
        var context = new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() };

        // Act
        var manual = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            context,
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);
        var automatic = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            context,
            automatic: true,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(manual.Results.Select(result => result.Release.ExternalId), Is.EqualTo(["good", "bad"]));
            Assert.That(automatic.Results.Select(result => result.Release.ExternalId), Is.EqualTo(["good"]));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecutePlanAsync_Succeeds_WhenOneConnectionCompletesWithNoAcceptedResults(bool automatic)
    {
        // Arrange
        _indexerConnections
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new IndexerConnection
                {
                    Id = Guid.NewGuid(),
                    IndexerId = _indexerId,
                    Name = "A failing connection",
                    Enabled = true,
                    Settings = new Dictionary<string, string> { ["outcome"] = "failure" },
                    CreatedAt = new DateTime(2026, 1, 1),
                },
                new IndexerConnection
                {
                    Id = Guid.NewGuid(),
                    IndexerId = _indexerId,
                    Name = "B empty connection",
                    Enabled = true,
                    Settings = new Dictionary<string, string> { ["outcome"] = "empty" },
                    CreatedAt = new DateTime(2026, 1, 1),
                },
            ]);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                ReleaseSearchRequest _,
                IReadOnlyDictionary<string, string> settings,
                CancellationToken _) => settings["outcome"] == "failure"
                    ? Task.FromException<IEnumerable<Release>>(new InvalidOperationException("failed"))
                    : Task.FromResult<IEnumerable<Release>>([]));
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results, Is.Empty);
            Assert.That(result.ConnectionsAttempted, Is.EqualTo(2));
            Assert.That(result.ConnectionsSucceeded, Is.EqualTo(1));
            Assert.That(result.Partial, Is.True);
        }
    }

    [Test]
    public async Task ExecutePlanAsync_ReturnsCompleteRootResults_WhenConnectionsExceedBranchMaximum()
    {
        // Arrange
        _indexerConnections
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new IndexerConnection
                {
                    Id = Guid.NewGuid(),
                    IndexerId = _indexerId,
                    Name = "A Connection",
                    Enabled = true,
                    Settings = new Dictionary<string, string> { ["name"] = "a" },
                    CreatedAt = new DateTime(2026, 1, 1),
                },
                new IndexerConnection
                {
                    Id = Guid.NewGuid(),
                    IndexerId = _indexerId,
                    Name = "B Connection",
                    Enabled = true,
                    Settings = new Dictionary<string, string> { ["name"] = "b" },
                    CreatedAt = new DateTime(2026, 1, 1),
                },
            ]);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                ReleaseSearchRequest _,
                IReadOnlyDictionary<string, string> settings,
                CancellationToken _) => Enumerable.Range(1, 3).Select(index => new Release
                {
                    Protocol = "torrent",
                    Title = $"{settings["name"]}-{index}",
                    ExternalId = $"{settings["name"]}-{index}",
                    PublishDate = new DateTime(2026, 1, 1),
                }));
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 3).ConfigureAwait(false);

        // Assert
        Assert.That(result.Results, Has.Count.EqualTo(6));
    }

    [Test]
    public async Task ExecutePlanAsync_EnrichesDuplicateObservation_WhenLaterAssessmentIsStronger()
    {
        // Arrange
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    IdSearchParameter = "imdbid",
                    IdSearchValue = "tt123",
                },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                _issuedRequests.Add(request))
            .ReturnsAsync((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                request.IdSearchParameter is null
                    ?
                    [
                        new Release
                        {
                            Protocol = "torrent",
                            Title = "Keyword",
                            ExternalId = "keyword",
                            PublishDate = new DateTime(2026, 1, 1),
                        },
                    ]
                    :
                    [
                        new Release
                        {
                            Protocol = "torrent",
                            Title = "Dune first",
                            ExternalId = "first",
                            PublishDate = new DateTime(2026, 1, 1),
                            IdentityKeys =
                            {
                                new ReleaseIdentityKey { Authority = "btih", Value = "shared-hash" },
                            },
                            ExternalIds =
                            {
                                new ExternalId { Source = "imdb", Value = "tt123" },
                            },
                        },
                        new Release
                        {
                            Protocol = "torrent",
                            Title = "Dune second",
                            ExternalId = "second",
                            PublishDate = new DateTime(2026, 1, 1),
                            IdentityKeys =
                            {
                                new ReleaseIdentityKey { Authority = "btih", Value = "shared-hash" },
                            },
                            ExternalIds =
                            {
                                new ExternalId { Source = "tmdb", Value = "456" },
                            },
                        },
                    ]);
        _releaseEvaluation
            .Setup(service => service.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()))
            .Returns((
                Release release,
                IReadOnlyList<ContentFact> _,
                ReleaseEvaluationContext _) => new ReleaseAssessment
                {
                    MatchResult = release.ExternalIds.Any(id => id.Source == "tmdb")
                        ? ReleaseMatchResult.Confident
                        : ReleaseMatchResult.NoMatch,
                });

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        var release = result.Results.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_issuedRequests, Has.Count.EqualTo(1));
            Assert.That(release.Assessment.MatchResult, Is.EqualTo(ReleaseMatchResult.Confident));
            Assert.That(release.Release.ExternalIds.Select(id => id.Source), Is.EquivalentTo(["imdb", "tmdb"]));
            Assert.That(release.Release.Sources, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_PreservesAllPlanNodeProvenance_WhenConnectionsReturnSameRelease()
    {
        // Arrange
        _indexerConnections
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new IndexerConnection
                {
                    Id = Guid.NewGuid(),
                    IndexerId = _indexerId,
                    Name = "A Connection",
                    Enabled = true,
                    Settings = new Dictionary<string, string> { ["name"] = "A" },
                    CreatedAt = new DateTime(2026, 1, 1),
                },
                new IndexerConnection
                {
                    Id = Guid.NewGuid(),
                    IndexerId = _indexerId,
                    Name = "B Connection",
                    Enabled = true,
                    Settings = new Dictionary<string, string> { ["name"] = "B" },
                    CreatedAt = new DateTime(2026, 1, 1),
                },
            ]);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                ReleaseSearchRequest _,
                IReadOnlyDictionary<string, string> settings,
                CancellationToken _) =>
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = settings["name"],
                    ExternalId = "same",
                    IdentityKeys = { new ReleaseIdentityKey { Authority = "btih", Value = "shared" }, },
                },
            ]);
        _releaseEvaluation
            .Setup(service => service.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()))
            .Returns((Release release, IReadOnlyList<ContentFact> _, ReleaseEvaluationContext _) =>
                new ReleaseAssessment
                {
                    MatchResult = ReleaseMatchResult.Confident,
                    MatchedRankOrdinal = release.Title == "B" ? 1 : 2,
                });
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "same" },
                ReleaseFacts = [new ContentFact { Value = "same" }],
            },
        };

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results, Has.Count.EqualTo(1));
            Assert.That(result.Results[0].Release.Title, Is.EqualTo("B"));
            Assert.That(result.Results[0].Assessment.MatchedRankOrdinal, Is.EqualTo(1));
            Assert.That(result.Results[0].PlanNodeIds,
                Is.EqualTo(["connection-1-request-1", "connection-2-request-1"]));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_DoesNotMutateCachedConnectionResult_WhenUnionOrCallerMutatesRelease()
    {
        // Arrange
        var includeSecondConnection = true;
        var firstConnectionId = Guid.NewGuid();
        _indexerConnections
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => includeSecondConnection
                ?
                [
                    new IndexerConnection
                    {
                        Id = firstConnectionId,
                        IndexerId = _indexerId,
                        Name = "A Connection",
                        Enabled = true,
                        Settings = new Dictionary<string, string> { ["name"] = "a" },
                        CreatedAt = new DateTime(2026, 1, 1),
                    },
                    new IndexerConnection
                    {
                        Id = Guid.NewGuid(),
                        IndexerId = _indexerId,
                        Name = "B Connection",
                        Enabled = true,
                        Settings = new Dictionary<string, string> { ["name"] = "b" },
                        CreatedAt = new DateTime(2026, 1, 1),
                    },
                ]
                :
                [
                    new IndexerConnection
                    {
                        Id = firstConnectionId,
                        IndexerId = _indexerId,
                        Name = "A Connection",
                        Enabled = true,
                        Settings = new Dictionary<string, string> { ["name"] = "a" },
                        CreatedAt = new DateTime(2026, 1, 1),
                    },
                ]);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                ReleaseSearchRequest _,
                IReadOnlyDictionary<string, string> settings,
                CancellationToken _) =>
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Dune",
                    ExternalId = settings["name"],
                    PublishDate = new DateTime(2026, 1, 1),
                    IdentityKeys =
                    {
                        new ReleaseIdentityKey { Authority = "btih", Value = "shared-hash" },
                    },
                },
            ]);
        using var cacheStore = new MemoryCacheStore(TimeProvider.System);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new ReleaseSearchQueryExecutor(
            _indexers.Object,
            _indexerConnections.Object,
            _eventBus.Object,
            [_indexerIntegration.Object],
            [],
            TimeProvider.System,
            NullLogger<ReleaseSearchQueryExecutor>.Instance,
            _releaseEvaluation.Object,
            cacheFactory,
            releaseComparers: [],
            searchOptions: new SearchOptions());
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };
        var first = await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);
        first.Results.Single().Release.Id = Guid.NewGuid();
        first.Results.Single().Release.Sources.First().Id =
            Guid.NewGuid();
        includeSecondConnection = false;

        // Act
        var second = await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        var release = second.Results.Single().Release;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(release.Id, Is.EqualTo(Guid.Empty));
            Assert.That(release.Sources, Has.Count.EqualTo(1));
            Assert.That(release.Sources.Single().ExternalId, Is.EqualTo("a"));
            Assert.That(release.Sources.Single().Id, Is.EqualTo(Guid.Empty));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_QueriesAgain_WhenCacheIsDisabled()
    {
        // Arrange
        var calls = 0;
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++calls == 1
                ? []
                :
                [
                    new Release
                    {
                        Protocol = "torrent",
                        Title = "Dune",
                        ExternalId = "fresh",
                        PublishDate = new DateTime(2026, 1, 1),
                    },
                ]);
        using var cacheStore = new MemoryCacheStore(TimeProvider.System);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new ReleaseSearchQueryExecutor(
            _indexers.Object,
            _indexerConnections.Object,
            _eventBus.Object,
            [_indexerIntegration.Object],
            [],
            TimeProvider.System,
            NullLogger<ReleaseSearchQueryExecutor>.Instance,
            _releaseEvaluation.Object,
            cacheFactory,
            releaseComparers: [],
            searchOptions: new SearchOptions());
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };
        await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20,
            useCache: false).ConfigureAwait(false);

        // Act
        var result = await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20,
            useCache: false).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(result.Results.Single().Release.ExternalId, Is.EqualTo("fresh"));
        }
    }

    [TestCase(2, 2)]
    [TestCase(900, 1)]
    public async Task ExecutePlanAsync_QueriesTheIndexerAgain_WhenTheCoalescingWindowElapsed(
        int coalescingWindowSeconds,
        int expectedQueries)
    {
        // Arrange
        var calls = 0;
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                calls++;
                return [];
            });
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var cacheStore = new MemoryCacheStore(timeProvider);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new ReleaseSearchQueryExecutor(
            _indexers.Object,
            _indexerConnections.Object,
            _eventBus.Object,
            [_indexerIntegration.Object],
            [],
            timeProvider,
            NullLogger<ReleaseSearchQueryExecutor>.Instance,
            _releaseEvaluation.Object,
            cacheFactory,
            [],
            new SearchOptions { CoalescingWindow = TimeSpan.FromSeconds(coalescingWindowSeconds) });
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };
        await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        // Act
        await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        Assert.That(calls, Is.EqualTo(expectedQueries));
    }

    [Test]
    public async Task ExecutePlanAsync_SkipsComparerForGroup_WhenOneReleaseIsNotComparable()
    {
        // Arrange
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "B",
                    ExternalId = "b",
                    PublishDate = new DateTime(2026, 1, 1),
                },
                new Release
                {
                    Protocol = "torrent",
                    Title = "A",
                    ExternalId = "a",
                    PublishDate = new DateTime(2026, 1, 1),
                },
                new Release
                {
                    Protocol = "torrent",
                    Title = "C",
                    ExternalId = "c",
                    PublishDate = new DateTime(2026, 1, 1),
                },
            ]);
        var comparer = new Mock<IReleaseComparer>();
        comparer.Setup(value => value.CanCompare(It.Is<Release>(release => release.ExternalId != "c")))
            .Returns(true);
        comparer.Setup(value => value.Compare(It.IsAny<Release>(), It.IsAny<Release>())).Returns(-1);
        var subject = new ReleaseSearchQueryExecutor(
            _indexers.Object,
            _indexerConnections.Object,
            _eventBus.Object,
            [_indexerIntegration.Object],
            [],
            TimeProvider.System,
            NullLogger<ReleaseSearchQueryExecutor>.Instance,
            _releaseEvaluation.Object,
            Mock.Of<ICacheFactory>(),
            [comparer.Object],
            searchOptions: new SearchOptions());
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };

        // Act
        var result = await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results.Select(value => value.Release.ExternalId), Is.EqualTo(["a", "b", "c"]));
            comparer.Verify(value => value.Compare(It.IsAny<Release>(), It.IsAny<Release>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecutePlanAsync_PreservesPlanOrder_WhenAllRankingKeysTie()
    {
        // Arrange
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Same",
                    Properties = "first",
                    PublishDate = new DateTime(2026, 1, 1),
                },
                new Release
                {
                    Protocol = "torrent",
                    Title = "Same",
                    Properties = "second",
                    PublishDate = new DateTime(2026, 1, 1),
                },
            ]);
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        Assert.That(result.Results.Select(value => value.Release.Properties), Is.EqualTo(["first", "second"]));
    }

    [Test]
    public async Task ExecutePlanAsync_RefreshesCachedResults_WhenLargerTargetRequestedAfterOversizedResponse()
    {
        // Arrange
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                _issuedRequests.Add(request))
            .ReturnsAsync(Enumerable.Range(1, 40).Select(index => new Release
            {
                Protocol = "torrent",
                Title = $"Release {index}",
                ExternalId = $"release-{index}",
                PublishDate = DateTime.UtcNow,
            }));
        using var cacheStore = new MemoryCacheStore(TimeProvider.System);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new ReleaseSearchQueryExecutor(
            _indexers.Object,
            _indexerConnections.Object,
            _eventBus.Object,
            [_indexerIntegration.Object],
            [],
            TimeProvider.System,
            NullLogger<ReleaseSearchQueryExecutor>.Instance,
            _releaseEvaluation.Object,
            cacheFactory,
            releaseComparers: [],
            searchOptions: new SearchOptions());
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };
        await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Act
        var result = await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 40).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_issuedRequests.Select(request => request.MaximumResults), Is.EqualTo([20, 40]));
            Assert.That(result.Results, Has.Count.EqualTo(40));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_RefreshesCachedResults_WhenCoverageIsInsufficient()
    {
        // Arrange
        var activities = new List<Activity>();
        var refreshMeasurements = new List<long>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == DataTelemetry.Name && instrument.Name == "magpie.cache.refresh")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            if (tags.ToArray().Any(tag => tag.Key == "cache" && Equals(tag.Value, "release-query")))
            {
                refreshMeasurements.Add(value);
            }
        });
        meterListener.Start();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ReleaseTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                _issuedRequests.Add(request))
            .ReturnsAsync((
                ReleaseSearchRequest request,
                IReadOnlyDictionary<string, string> _,
                CancellationToken _) => Enumerable.Range(1, request.MaximumResults).Select(index => new Release
                {
                    Protocol = "torrent",
                    Title = $"Release {index}",
                    ExternalId = $"release-{index}",
                    PublishDate = DateTime.UtcNow,
                }));
        using var cacheStore = new MemoryCacheStore(TimeProvider.System);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new ReleaseSearchQueryExecutor(
            _indexers.Object,
            _indexerConnections.Object,
            _eventBus.Object,
            [_indexerIntegration.Object],
            [],
            TimeProvider.System,
            NullLogger<ReleaseSearchQueryExecutor>.Instance,
            _releaseEvaluation.Object,
            cacheFactory,
            releaseComparers: [],
            searchOptions: new SearchOptions());
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };
        await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Act
        var result = await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 40).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_issuedRequests.Select(request => request.MaximumResults), Is.EqualTo([20, 40]));
            Assert.That(result.Results, Has.Count.EqualTo(40));
            Assert.That(activities
                .Where(activity => activity.OperationName == "magpie.release-query.node.keyword" &&
                    Equals(activity.GetTagItem("indexer.connection.id"), _connectionId.ToString()))
                .Select(activity => activity.GetTagItem("magpie.cache.outcome")),
                Is.EqualTo(["miss", "refresh"]));
            Assert.That(activities
                .Where(activity => activity.OperationName == "magpie.release-query.node.keyword" &&
                    Equals(activity.GetTagItem("indexer.connection.id"), _connectionId.ToString()))
                .Select(activity => activity.GetTagItem("magpie.cache.hit")),
                Is.EqualTo([false, false]));
            Assert.That(refreshMeasurements, Is.EqualTo([1]));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_DoesNotPublishRefreshedCoverage_WhenCallerCancelsDuringPopulation()
    {
        // Arrange
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
            {
                calls++;
                if (calls == 2)
                {
                    refreshStarted.SetResult();
                    await completeRefresh.Task.ConfigureAwait(false);
                }

                return Enumerable.Range(1, request.MaximumResults).Select(index => new Release
                {
                    Protocol = "torrent",
                    Title = $"Release {index}",
                    ExternalId = $"release-{index}",
                });
            });
        using var cacheStore = new MemoryCacheStore(TimeProvider.System);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new ReleaseSearchQueryExecutor(
            _indexers.Object,
            _indexerConnections.Object,
            _eventBus.Object,
            [_indexerIntegration.Object],
            [],
            TimeProvider.System,
            NullLogger<ReleaseSearchQueryExecutor>.Instance,
            _releaseEvaluation.Object,
            cacheFactory,
            releaseComparers: [],
            searchOptions: new SearchOptions());
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };
        await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        var canceledRefresh = subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 40,
            cancellationToken: cancellation.Token);
        await refreshStarted.Task.ConfigureAwait(false);

        // Act
        await cancellation.CancelAsync().ConfigureAwait(false);
        completeRefresh.SetResult();
        async Task Act() => await canceledRefresh.ConfigureAwait(false);
        Assert.That(Act, Throws.InstanceOf<OperationCanceledException>());
        var result = await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 40).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls, Is.EqualTo(3));
            Assert.That(result.Results, Has.Count.EqualTo(40));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_ReusesSparseCachedResults_WhenTheTargetAlreadyHasCachedResults()
    {
        // Arrange
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                _issuedRequests.Add(request))
            .ReturnsAsync(Enumerable.Range(1, 10).Select(index => new Release
            {
                Protocol = "torrent",
                Title = $"Release {index}",
                ExternalId = $"release-{index}",
                PublishDate = DateTime.UtcNow,
            }));
        using var cacheStore = new MemoryCacheStore(TimeProvider.System);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new ReleaseSearchQueryExecutor(
            _indexers.Object,
            _indexerConnections.Object,
            _eventBus.Object,
            [_indexerIntegration.Object],
            [],
            TimeProvider.System,
            NullLogger<ReleaseSearchQueryExecutor>.Instance,
            _releaseEvaluation.Object,
            cacheFactory,
            releaseComparers: [],
            searchOptions: new SearchOptions());
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };
        await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Act
        var result = await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 40).ConfigureAwait(false);
        await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 40).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_issuedRequests.Select(request => request.MaximumResults), Is.EqualTo([20, 40]));
            Assert.That(result.Results, Has.Count.EqualTo(10));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_DoesNotReuseCachedResults_WhenSettingsOrRequestDiffer()
    {
        // Arrange
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ReleaseTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var endpoint = "first";
        _indexerConnections
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            [
                new IndexerConnection
                {
                    Id = _connectionId,
                    IndexerId = _indexerId,
                    Name = "Test Connection",
                    Enabled = true,
                    Settings = new Dictionary<string, string> { ["endpoint"] = endpoint },
                    CreatedAt = DateTime.UtcNow,
                },
            ]);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                _issuedRequests.Add(request))
            .ReturnsAsync([]);
        using var cacheStore = new MemoryCacheStore(TimeProvider.System);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new ReleaseSearchQueryExecutor(
            _indexers.Object,
            _indexerConnections.Object,
            _eventBus.Object,
            [_indexerIntegration.Object],
            [],
            TimeProvider.System,
            NullLogger<ReleaseSearchQueryExecutor>.Instance,
            _releaseEvaluation.Object,
            cacheFactory,
            releaseComparers: [],
            searchOptions: new SearchOptions());
        var firstRoute = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };
        var context = new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() };
        await subject.ExecutePlanAsync(
            firstRoute,
            [],
            new HashSet<Guid> { _indexerId },
            context,
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);
        endpoint = "second";
        await subject.ExecutePlanAsync(
            firstRoute,
            [],
            new HashSet<Guid> { _indexerId },
            context,
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);
        var secondRoute = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Foundation" },
                ReleaseFacts = [new ContentFact { Value = "Foundation" }],
            },
        };

        // Act
        await subject.ExecutePlanAsync(
            secondRoute,
            [],
            new HashSet<Guid> { _indexerId },
            context,
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            static string? PlanFingerprint(Activity activity)
            {
                var json = (string)activity.Events.Single(activityEvent => activityEvent.Name == "plan")
                    .Tags.Single(tag => tag.Key == "magpie.query.plan_json").Value!;
                using var document = JsonDocument.Parse(json);
                return document.RootElement.GetProperty("fingerprint").GetString();
            }

            Assert.That(_issuedRequests, Has.Count.EqualTo(3));
            Assert.That(_issuedRequests.Select(request => request.Title),
                Is.EqualTo(["Dune", "Dune", "Foundation"]));
            var fingerprints = activities
                .Where(activity => activity.OperationName == "magpie.release-query.execute" &&
                    activity.Events.Any(activityEvent => activityEvent.Tags.Any(tag =>
                        tag.Key == "magpie.query.plan_json" &&
                        tag.Value is string json &&
                        json.Contains(context.ContentHierarchyItemId.ToString(), StringComparison.OrdinalIgnoreCase))))
                .Select(PlanFingerprint)
                .ToArray();
            Assert.That(fingerprints[1], Is.EqualTo(fingerprints[0]));
            Assert.That(fingerprints[2], Is.Not.EqualTo(fingerprints[0]));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_EvaluatesKeywordChildrenRoundRobin_WhenEarlierChildHasRejectedResults()
    {
        // Arrange
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "first" },
                ReleaseFacts = [new ContentFact { Value = "first" }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "second" },
                ReleaseFacts = [new ContentFact { Value = "second" }],
            },
        };
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                request.Title == "first"
                    ?
                    [
                        new Release { Protocol = "torrent", Title = "first-1", ExternalId = "first-1" },
                        new Release { Protocol = "torrent", Title = "first-2", ExternalId = "first-2" },
                    ]
                    :
                    [
                        new Release { Protocol = "torrent", Title = "second-1", ExternalId = "second-1" },
                    ]);
        _releaseEvaluation
            .Setup(service => service.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()))
            .Returns((Release release, IReadOnlyList<ContentFact> _, ReleaseEvaluationContext _) =>
                new ReleaseAssessment
                {
                    MatchResult = release.ExternalId == "second-1"
                        ? ReleaseMatchResult.Confident
                        : ReleaseMatchResult.NoMatch,
                });

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 2).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results.Select(value => value.Release.ExternalId),
                Is.EquivalentTo(["first-1", "second-1"]));
            _indexerIntegration.Verify(integration => integration.SearchAsync(
                It.Is<ReleaseSearchRequest>(request => request.Title == "first"),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            _indexerIntegration.Verify(integration => integration.SearchAsync(
                It.Is<ReleaseSearchRequest>(request => request.Title == "second"),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ExecutePlanAsync_EvaluatesCrossChildDuplicateOnce_WhenBranchesShareAResult()
    {
        // Arrange
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "first" },
                ReleaseFacts = [new ContentFact { Value = "first" }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "second" },
                ReleaseFacts = [new ContentFact { Value = "second" }],
            },
        };
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                request.Title == "first"
                    ?
                    [
                        new Release
                        {
                            Protocol = "torrent",
                            Title = "X",
                            ExternalId = "x-first",
                            IdentityKeys = { new ReleaseIdentityKey { Authority = "btih", Value = "x" }, },
                        },
                        new Release
                        {
                            Protocol = "torrent",
                            Title = "Y",
                            ExternalId = "y",
                            IdentityKeys = { new ReleaseIdentityKey { Authority = "btih", Value = "y" }, },
                        },
                    ]
                    :
                    [
                        new Release
                        {
                            Protocol = "torrent",
                            Title = "X duplicate",
                            ExternalId = "x-second",
                            IdentityKeys = { new ReleaseIdentityKey { Authority = "btih", Value = "x" }, },
                        },
                    ]);

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 2).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results.Select(value => value.Release.Title), Is.EqualTo(["X", "Y"]));
            Assert.That(result.Results[0].PlanNodeIds,
                Is.EqualTo(["connection-1-request-1", "connection-1-request-2"]));
            _releaseEvaluation.Verify(service => service.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()), Times.Exactly(2));
        }
    }

    [TestCase("weak", "strong")]
    [TestCase("strong", "weak")]
    public async Task ExecutePlanAsync_EvaluatesDuplicateOnce_WhenRoutesShareAResult(
        string first,
        string second)
    {
        // Arrange
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = first },
                ReleaseFacts = [new ContentFact { Value = first }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = second },
                ReleaseFacts = [new ContentFact { Value = second }],
            },
        };
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = request.Title!,
                    ExternalId = request.Title!,
                    IdentityKeys = { new ReleaseIdentityKey { Authority = "btih", Value = "shared" }, },
                },
            ]);
        _releaseEvaluation
            .Setup(service => service.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()))
            .Returns((Release _, IReadOnlyList<ContentFact> facts, ReleaseEvaluationContext _) =>
                new ReleaseAssessment
                {
                    MatchResult = facts.Any(fact => fact.Value == "strong")
                        ? ReleaseMatchResult.Confident
                        : ReleaseMatchResult.NoMatch,
                });

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: true,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results, Has.Count.EqualTo(1));
            Assert.That(result.Results[0].Assessment.MatchResult, Is.EqualTo(ReleaseMatchResult.Confident));
            Assert.That(result.Results[0].ReleaseFacts.Select(fact => fact.Value), Is.EqualTo([first, second]));
            Assert.That(result.Results[0].PlanNodeIds,
                Is.EqualTo(["connection-1-request-1", "connection-1-request-2"]));
            _releaseEvaluation.Verify(service => service.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()), Times.Once);
        }
    }

    [Test]
    public void ExecutePlanAsync_StopsConnectionAndFailsSearch_WhenRequestFailsAfterAcceptedObservation()
    {
        // Arrange
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Returns((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                request.Title == "second"
                    ? Task.FromException<IEnumerable<Release>>(new InvalidOperationException("provider failed"))
                    : Task.FromResult<IEnumerable<Release>>(
                    [
                        new Release { Protocol = "torrent", Title = "accepted", ExternalId = "accepted" },
                    ]));
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "first" },
                ReleaseFacts = [new ContentFact { Value = "first" }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "second" },
                ReleaseFacts = [new ContentFact { Value = "second" }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "third" },
                ReleaseFacts = [new ContentFact { Value = "third" }],
            },
        };

        // Act
        async Task Act() => await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: true,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.ThrowsAsync<InvalidOperationException>(Act);
            _releaseEvaluation.Verify(service => service.AssessMatch(
                It.Is<Release>(release => release.ExternalId == "accepted"),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()), Times.Once);
            _indexerIntegration.Verify(integration => integration.SearchAsync(
                It.Is<ReleaseSearchRequest>(request => request.Title == "third"),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecutePlanAsync_PreservesEvaluationCapacity_WhenAnObservationIsDuplicated()
    {
        // Arrange
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "A first",
                    ExternalId = "a-first",
                    IdentityKeys = { new ReleaseIdentityKey { Authority = "btih", Value = "a" }, },
                },
                new Release
                {
                    Protocol = "torrent",
                    Title = "A duplicate",
                    ExternalId = "a-duplicate",
                    IdentityKeys = { new ReleaseIdentityKey { Authority = "btih", Value = "a" }, },
                },
                new Release
                {
                    Protocol = "torrent",
                    Title = "B",
                    ExternalId = "b",
                    IdentityKeys = { new ReleaseIdentityKey { Authority = "btih", Value = "b" }, },
                },
            ]);
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "test" },
                ReleaseFacts = [new ContentFact { Value = "test" }],
            },
        };

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 2).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results.Select(value => value.Release.Title), Is.EqualTo(["A first", "B"]));
            _releaseEvaluation.Verify(service => service.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()), Times.Exactly(2));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_CollapsesResults_WhenIdentityMatchesFormATransitiveBridge()
    {
        // Arrange
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "A",
                    ExternalId = "a",
                    IdentityKeys = { new ReleaseIdentityKey { Authority = "btih", Value = "x" }, },
                },
                new Release
                {
                    Protocol = "torrent",
                    Title = "B",
                    ExternalId = "b",
                    IdentityKeys = { new ReleaseIdentityKey { Authority = "guid", Value = "y" }, },
                },
                new Release
                {
                    Protocol = "torrent",
                    Title = "C",
                    ExternalId = "c",
                    IdentityKeys =
                    {
                        new ReleaseIdentityKey { Authority = "btih", Value = "x" },
                        new ReleaseIdentityKey { Authority = "guid", Value = "y" },
                    },
                },
            ]);
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "test" },
                ReleaseFacts = [new ContentFact { Value = "test" }],
            },
        };

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results, Has.Count.EqualTo(1));
            Assert.That(result.Results[0].Release.IdentityKeys, Has.Count.EqualTo(2));
            Assert.That(result.Results[0].Release.Sources, Has.Count.EqualTo(3));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_RecordsCanceledTerminalState_WhenProviderRequestIsCanceled()
    {
        // Arrange
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ReleaseTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (ReleaseSearchRequest _, IReadOnlyDictionary<string, string> _, CancellationToken token) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return [];
            });
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "test" },
                ReleaseFacts = [new ContentFact { Value = "test" }],
            },
        };
        using var cancellation = new CancellationTokenSource();
        var execution = _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20,
            cancellationToken: cancellation.Token);
        await started.Task.ConfigureAwait(false);

        // Act
        await cancellation.CancelAsync().ConfigureAwait(false);
        async Task Act() => await execution.ConfigureAwait(false);

        // Assert
        Assert.That(Act, Throws.InstanceOf<OperationCanceledException>());
        var affected = activities.Where(activity => activity.OperationName is
            "magpie.release-query.execute" or
            "magpie.release-query.node.coalesce" or
            "magpie.release-query.node.union" or
            "magpie.release-query.node.external-id" or
            "magpie.release-query.node.keyword" or
            "magpie.release-query.request").ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(affected, Is.Not.Empty);
            Assert.That(affected.All(activity =>
                Equals(activity.GetTagItem("magpie.query.terminal_reason"), "canceled")), Is.True);
        }
    }

    [Test]
    public void ExecutePlanAsync_ExportsOnlyClassifiedFailure_WhenProviderMessageContainsSecret()
    {
        // Arrange
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ReleaseTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var testLogger = new Mock<ILogger<ReleaseSearchQueryExecutor>>();
        var subject = new ReleaseSearchQueryExecutor(
            _indexers.Object,
            _indexerConnections.Object,
            _eventBus.Object,
            [_indexerIntegration.Object],
            [],
            TimeProvider.System,
            testLogger.Object,
            _releaseEvaluation.Object,
            cacheFactory: Mock.Of<ICacheFactory>(),
            releaseComparers: [],
            searchOptions: new SearchOptions());
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("https://tracker.example/query?passkey=secret-value"));
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "test" },
                ReleaseFacts = [new ContentFact { Value = "test" }],
            },
        };

        // Act
        async Task Act() => await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        Assert.ThrowsAsync<InvalidOperationException>(Act);
        var traceText = string.Join("\n", activities.SelectMany(activity =>
            activity.TagObjects.Select(tag => tag.Value?.ToString())
                .Append(activity.StatusDescription)));
        var logText = string.Join("\n", testLogger.Invocations
            .SelectMany(invocation => invocation.Arguments)
            .Select(argument => argument?.ToString()));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(traceText, Does.Not.Contain("secret-value").And.Not.Contain("tracker.example"));
            Assert.That(logText, Does.Not.Contain("secret-value").And.Not.Contain("tracker.example"));
            _indexerConnections.Verify(repository => repository.PatchAsync(
                It.Is<IndexerConnectionPatch>(patch =>
                    patch.Id == _connectionId &&
                    patch.LastError != null &&
                    patch.LastError.Value == "provider-error:InvalidOperationException"),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ExecutePlanAsync_RecordsConnectionAsNotExecutable_WhenContentTypeIsNotApplicable()
    {
        // Arrange
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ReleaseTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "test" },
                ReleaseFacts = [new ContentFact { Value = "test" }],
            },
        };

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid>(),
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        var root = activities.Single(activity => activity.OperationName == "magpie.release-query.execute");
        var planEvent = root.Events.Single(activityEvent => activityEvent.Name == "plan");
        var planJson = (string)planEvent.Tags.Single(tag => tag.Key == "magpie.query.plan_json").Value!;
        using var plan = JsonDocument.Parse(planJson);
        var connection = plan.RootElement.GetProperty("root").GetProperty("children")[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ConnectionsAttempted, Is.Zero);
            Assert.That(result.Warning, Is.EqualTo("no-indexers-available"));
            Assert.That(connection.GetProperty("executable").GetBoolean(), Is.False);
            Assert.That(connection.GetProperty("skippedReason").GetString(),
                Is.EqualTo("content-type-not-applicable"));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_ReportsNoIndexersConfigured_WhenPlanHasNoConnections()
    {
        // Arrange
        _indexers
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _indexerConnections
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "test" },
                ReleaseFacts = [new ContentFact { Value = "test" }],
            },
        };

        // Act
        var result = await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid>(),
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        Assert.That(result.Warning, Is.EqualTo("no-indexers-configured"));
    }

    [Test]
    public async Task ExecutePlanAsync_ReportsQueryNotSupported_WhenOnlyUnavailableConnectionSupportsRoute()
    {
        // Arrange
        var unavailableIndexerId = Guid.NewGuid();
        _indexers
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Indexer
                {
                    Id = _indexerId,
                    ProviderType = "test-indexer",
                    Name = "Available",
                    CreatedAt = new DateTime(2026, 1, 1),
                },
                new Indexer
                {
                    Id = unavailableIndexerId,
                    ProviderType = "unavailable-indexer",
                    Name = "Unavailable",
                    CreatedAt = new DateTime(2026, 1, 1),
                },
            ]);
        _indexerConnections
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new IndexerConnection
                {
                    Id = _connectionId,
                    IndexerId = _indexerId,
                    Name = "Available",
                    Enabled = true,
                    CreatedAt = new DateTime(2026, 1, 1),
                },
                new IndexerConnection
                {
                    Id = Guid.NewGuid(),
                    IndexerId = unavailableIndexerId,
                    Name = "Unavailable",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Unhealthy,
                    CreatedAt = new DateTime(2026, 1, 1),
                },
            ]);
        _indexerIntegration
            .SetupGet(integration => integration.SearchFacets)
            .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "q" },]);
        var unavailableIntegration = new Mock<IIndexerIntegration>();
        unavailableIntegration.SetupGet(integration => integration.ProviderType).Returns("unavailable-indexer");
        unavailableIntegration.SetupGet(integration => integration.SearchFacets)
            .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "imdbid" },]);
        unavailableIntegration.SetupGet(integration => integration.SupportsKeywordSearch).Returns(true);
        var subject = new ReleaseSearchQueryExecutor(
            _indexers.Object,
            _indexerConnections.Object,
            _eventBus.Object,
            [_indexerIntegration.Object, unavailableIntegration.Object],
            [],
            TimeProvider.System,
            NullLogger<ReleaseSearchQueryExecutor>.Instance,
            _releaseEvaluation.Object,
            cacheFactory: Mock.Of<ICacheFactory>(),
            releaseComparers: [],
            searchOptions: new SearchOptions());
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ReleaseTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    IdSearchParameter = "imdbid",
                    IdSearchValue = "tt123",
                },
                ReleaseFacts = [new ContentFact { Value = "test" }],
            },
        };

        // Act
        var result = await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId, unavailableIndexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        var root = activities.Single(activity => activity.OperationName == "magpie.release-query.execute");
        var planEvent = root.Events.Single(activityEvent => activityEvent.Name == "plan");
        var planJson = (string)planEvent.Tags.Single(tag => tag.Key == "magpie.query.plan_json").Value!;
        using var plan = JsonDocument.Parse(planJson);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Warning, Is.EqualTo("query-not-supported"));
            Assert.That(plan.RootElement.GetProperty("warnings")[0].GetString(),
                Is.EqualTo("query-not-supported"));
            Assert.That(plan.RootElement.GetProperty("root").GetProperty("children").GetArrayLength(),
                Is.EqualTo(2));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_EmitsPlanAndCorrelatedActivities_WhenCoalesceUsesKeywordUnion()
    {
        // Arrange
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ReleaseTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        _indexerIntegration
            .SetupGet(integration => integration.SearchFacets)
            .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "q", "imdbId" },]);
        _indexerConnections
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new IndexerConnection
                {
                    Id = _connectionId,
                    IndexerId = _indexerId,
                    Name = "Test Connection",
                    Enabled = true,
                    Settings = new Dictionary<string, string> { ["apiKey"] = "secret-value" },
                    CreatedAt = new DateTime(2026, 1, 1),
                },
            ]);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                ReleaseSearchRequest request,
                IReadOnlyDictionary<string, string> _,
                CancellationToken _) =>
                request.IdSearchParameter is not null
                    ? []
                    :
                    [
                        new Release
                        {
                            Protocol = "torrent",
                            Title = "Dune",
                            ExternalId = "keyword",
                            PublishDate = new DateTime(2026, 1, 1),
                        },
                    ]);
        _releaseEvaluation
            .Setup(service => service.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()))
            .Returns((
                Release release,
                IReadOnlyList<ContentFact> _,
                ReleaseEvaluationContext _) => new ReleaseAssessment
                {
                    MatchResult = release.ExternalId == "keyword"
                        ? ReleaseMatchResult.Confident
                        : ReleaseMatchResult.NoMatch,
                });
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    IdSearchParameter = "imdbid",
                    IdSearchValue = "tt123",
                },
                ReleaseFacts = [new ContentFact { Value = "identity" }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "keyword" }],
            },
        };

        // Act
        await _subject.ExecutePlanAsync(
            routes,
            ["movies"],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        var root = activities.Single(activity =>
            activity.OperationName == "magpie.release-query.execute" &&
            Equals(activity.GetTagItem("magpie.query.node.id"), "root"));
        var coalesce = activities.Single(activity =>
            activity.OperationName == "magpie.release-query.node.coalesce" &&
            Equals(activity.GetTagItem("magpie.query.node.id"), "connection-1"));
        var keywordUnion = activities.Single(activity =>
            activity.OperationName == "magpie.release-query.node.union" &&
            Equals(activity.GetTagItem("magpie.query.node.id"), "connection-1-keywords"));
        var routeNodes = activities
            .Where(activity => activity.OperationName is
                "magpie.release-query.node.external-id" or "magpie.release-query.node.keyword")
            .OrderBy(activity => activity.GetTagItem("magpie.query.node.id"))
            .ToArray();
        var requests = activities
            .Where(activity => activity.OperationName == "magpie.release-query.request")
            .OrderBy(activity => activity.GetTagItem("magpie.request.role"))
            .ToArray();
        var evaluations = activities
            .Where(activity => activity.OperationName == "magpie.release-query.evaluation")
            .ToArray();
        var planEvent = root.Events.Single(activityEvent => activityEvent.Name == "plan");
        var planJson = (string)planEvent.Tags.Single(tag => tag.Key == "magpie.query.plan_json").Value!;
        using var plan = JsonDocument.Parse(planJson);
        var rootPlan = plan.RootElement.GetProperty("root");
        var children = rootPlan.GetProperty("children")[0].GetProperty("children");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.RootElement.GetProperty("provider").GetString(), Is.EqualTo("magpie.release-search"));
            Assert.That(plan.RootElement.GetProperty("fingerprint").GetString(), Is.Not.Empty);
            Assert.That(plan.RootElement.GetProperty("warnings").ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(rootPlan.GetProperty("children").GetArrayLength(), Is.EqualTo(1));
            Assert.That(children[0].GetProperty("kind").GetString(), Is.EqualTo("external-id"));
            Assert.That(children[0].GetProperty("precondition").GetProperty("kind").GetString(),
                Is.EqualTo("always"));
            Assert.That(children[0].GetProperty("routePlan").GetProperty("request")
                .GetProperty("idSearchParameter").GetString(),
                Is.EqualTo("imdbId"));
            Assert.That(children[0].GetProperty("routePlan").GetProperty("request")
                .GetProperty("categories")[0].GetString(),
                Is.EqualTo("movies"));
            Assert.That(children[0].GetProperty("routePlan").GetProperty("releaseFacts")[0]
                .GetProperty("value").GetString(),
                Is.EqualTo("identity"));
            Assert.That(children[1].GetProperty("kind").GetString(), Is.EqualTo("union"));
            Assert.That(children[1].GetProperty("precondition").GetProperty("kind").GetString(),
                Is.EqualTo("nodeNotAccepted"));
            Assert.That(children[1].GetProperty("precondition").GetProperty("nodeId").GetString(),
                Is.EqualTo("connection-1-request-1"));
            Assert.That(children[1].GetProperty("children")[0].GetProperty("kind").GetString(),
                Is.EqualTo("keyword"));
            Assert.That(planJson, Does.Not.Contain("\"evaluation\"").And.Not.Contain("secret-value"));
            Assert.That(root.GetTagItem("magpie.query.plan_json"), Is.EqualTo(planJson));
            Assert.That(coalesce.ParentId, Is.EqualTo(root.Id));
            Assert.That(keywordUnion.ParentId, Is.EqualTo(coalesce.Id));
            Assert.That(routeNodes, Has.Length.EqualTo(2));
            Assert.That(routeNodes[0].ParentId, Is.EqualTo(coalesce.Id));
            Assert.That(routeNodes[1].ParentId, Is.EqualTo(keywordUnion.Id));
            Assert.That(routeNodes.All(node => node.GetTagItem("magpie.query.request_json") is string), Is.True);
            Assert.That(requests, Has.Length.EqualTo(2));
            Assert.That(requests.Select(request => request.ParentId),
                Is.EquivalentTo(routeNodes.Select(node => node.Id)));
            Assert.That(requests.All(request => request.GetTagItem("magpie.request.target") is string), Is.True);
            Assert.That(evaluations, Is.Empty);
            Assert.That(new[] { root, coalesce, keywordUnion }.All(activity =>
                activity.GetTagItem("magpie.query.contributions.new") is not null &&
                activity.GetTagItem("magpie.query.evaluated_results") is not null &&
                activity.GetTagItem("magpie.query.accepted_results") is not null &&
                activity.GetTagItem("magpie.query.rejected_results") is not null &&
                activity.GetTagItem("magpie.query.terminal_reason") is not null), Is.True);
            Assert.That(coalesce.GetTagItem("magpie.query.terminal_reason"), Is.EqualTo("accepted"));
            Assert.That(root.GetTagItem("magpie.query.accepted_results"), Is.EqualTo(1));
            Assert.That(root.GetTagItem("magpie.query.rejected_results"), Is.EqualTo(0));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_KeepsUnsupportedRouteInPlan_WhenAnotherRouteIsSupported()
    {
        // Arrange
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ReleaseTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        _indexerIntegration
            .SetupGet(integration => integration.SearchFacets)
            .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "q" },]);
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { IdSearchParameter = "imdbid", IdSearchValue = "tt123" },
                ReleaseFacts = [new ContentFact { Facets = new Dictionary<string, string> { ["imdb"] = "tt123" } }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "Dune" },
                ReleaseFacts = [new ContentFact { Value = "Dune" }],
            },
        };

        // Act
        await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        var root = activities.Single(activity => activity.OperationName == "magpie.release-query.execute");
        var json = (string)root.Events.Single(activityEvent => activityEvent.Name == "plan")
            .Tags.Single(tag => tag.Key == "magpie.query.plan_json").Value!;
        using var plan = JsonDocument.Parse(json);
        var children = plan.RootElement.GetProperty("root").GetProperty("children")[0]
            .GetProperty("children");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(children[0].GetProperty("kind").GetString(), Is.EqualTo("external-id"));
            Assert.That(children[0].GetProperty("executable").GetBoolean(), Is.False);
            Assert.That(children[0].GetProperty("warnings")[0].GetString(), Is.EqualTo("query-not-supported"));
            Assert.That(children[1].GetProperty("children")[0].GetProperty("kind").GetString(),
                Is.EqualTo("keyword"));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_EmitsSkippedNodes_WhenUsefulIdPrunesRemainingPlan()
    {
        // Arrange
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ReleaseTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release { Protocol = "torrent", Title = "identity", ExternalId = "identity" },
            ]);
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    IdSearchParameter = "imdbid",
                    IdSearchValue = "tt123",
                },
                ReleaseFacts = [new ContentFact { Value = "first-id" }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest
                {
                    IdSearchParameter = "imdbid",
                    IdSearchValue = "tt456",
                },
                ReleaseFacts = [new ContentFact { Value = "second-id" }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "keyword" },
                ReleaseFacts = [new ContentFact { Value = "keyword" }],
            },
        };

        // Act
        await _subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        var coalesce = activities.Single(activity =>
            activity.OperationName == "magpie.release-query.node.coalesce");
        var skippedNodeIds = coalesce.Events
            .Where(activityEvent => activityEvent.Name == "node.skipped")
            .Select(activityEvent => activityEvent.Tags.Single(tag => tag.Key == "magpie.query.node.id").Value)
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(skippedNodeIds, Is.EqualTo([
                "connection-1-request-2",
                "connection-1-keywords",
                "connection-1-request-3",
            ]));
            Assert.That(coalesce.Events.Where(activityEvent => activityEvent.Name == "node.skipped")
                .All(activityEvent => activityEvent.Tags.Any(tag =>
                    tag.Key == "magpie.query.node.reason" && Equals(tag.Value, "coalesce-satisfied"))), Is.True);
            Assert.That(
                activities,
                Has.Exactly(1).Matches<Activity>(
                    activity => activity.OperationName == "magpie.release-query.request"));
        }
    }

    [Test]
    public async Task ExecutePlanAsync_RecordsMemoryCacheOutcomeWithoutSensitiveData_WhenRequestRunsTwice()
    {
        // Arrange
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ReleaseTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var cacheStore = new MemoryCacheStore(TimeProvider.System);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new ReleaseSearchQueryExecutor(
            _indexers.Object,
            _indexerConnections.Object,
            _eventBus.Object,
            [_indexerIntegration.Object],
            [],
            TimeProvider.System,
            NullLogger<ReleaseSearchQueryExecutor>.Instance,
            _releaseEvaluation.Object,
            cacheFactory,
            releaseComparers: [],
            searchOptions: new SearchOptions());
        var routes = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "sensitive title" },
                ReleaseFacts = [new ContentFact { Value = "sensitive title" }],
            },
        };

        // Act
        await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);
        await subject.ExecutePlanAsync(
            routes,
            [],
            new HashSet<Guid> { _indexerId },
            new ReleaseEvaluationContext { ContentHierarchyItemId = Guid.NewGuid() },
            automatic: false,
            maximumResults: 20).ConfigureAwait(false);

        // Assert
        var requests = activities.Where(activity =>
            activity.OperationName == "magpie.release-query.node.keyword" &&
            Equals(activity.GetTagItem("indexer.connection.id"), _connectionId.ToString())).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests.Select(request => request.GetTagItem("magpie.cache.hit")),
                Is.EqualTo([false, true]));
            Assert.That(requests.Select(request => request.GetTagItem("magpie.cache.outcome")),
                Is.EqualTo(["miss", "hit"]));
            Assert.That(requests.All(request => Equals(
                request.GetTagItem("magpie.cache.name"), "release-query")), Is.True);
            Assert.That(requests.All(request => Equals(
                request.GetTagItem("magpie.cache.lifetime"), "shared")), Is.True);
            Assert.That(requests.All(request => Equals(
                request.GetTagItem("magpie.cache.backend"), "memory")), Is.True);
            Assert.That(requests.All(request => Equals(
                request.GetTagItem("magpie.cache.level"), "memory")), Is.True);
            Assert.That(requests.SelectMany(request => request.TagObjects)
                .Any(tag => Equals(tag.Value, "sensitive title")), Is.False);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsTheConnectionInLaterPhases_WhenItFailedInAnEarlierPhase()
    {
        // Arrange
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("indexer unreachable"));
        var phases = new[]
        {
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "pilot" },
                ReleaseFacts = [new ContentFact { Value = "pilot" }],
            },
            new ReleaseSearchPhase
            {
                Request = new ReleaseSearchRequest { Title = "pilot 2019" },
                ReleaseFacts = [new ContentFact { Value = "pilot 2019" }],
            },
        };

        // Act
        var result = await _subject.ExecuteAsync(phases, [], new HashSet<Guid> { _indexerId }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[1].ConnectionsAttempted, Is.Zero);
            Assert.That(result[1].Connections.Single().SkippedReason, Is.EqualTo("failed-earlier-phase"));
            _indexerIntegration.Verify(
                integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
