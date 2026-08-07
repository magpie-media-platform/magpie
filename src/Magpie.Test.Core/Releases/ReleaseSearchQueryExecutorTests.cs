using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Releases;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Releases;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Releases;

[TestFixture]
public sealed class ReleaseSearchQueryExecutorTests
{
    private Mock<IIndexerRepository> _indexers = null!;
    private Mock<IIndexerConnectionRepository> _indexerConnections = null!;
    private Mock<IIndexerService> _indexerService = null!;
    private Mock<IIndexerIntegration> _indexerIntegration = null!;
    private IReleaseSearchQueryExecutor _subject = null!;
    private List<ReleaseSearchRequest> _issuedRequests = null!;
    private Guid _indexerId;
    private Guid _connectionId;

    [SetUp]
    public void SetUp()
    {
        _indexerId = Guid.NewGuid();
        _connectionId = Guid.NewGuid();
        _issuedRequests = [];

        _indexers = new Mock<IIndexerRepository>();
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

        _indexerConnections = new Mock<IIndexerConnectionRepository>();
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

        _indexerService = new Mock<IIndexerService>();
        _indexerService
            .Setup(service => service.PatchConnectionAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Magpie.Models.Arguments.IndexerConnectionPatch>(),
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

        _subject = new ReleaseSearchQueryExecutor(
            _indexers.Object,
            _indexerConnections.Object,
            _indexerService.Object,
            [_indexerIntegration.Object],
            [],
            TimeProvider.System,
            NullLogger<ReleaseSearchQueryExecutor>.Instance);
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
            _indexerService.Object,
            [_indexerIntegration.Object, healthyIndexerIntegration.Object],
            [],
            TimeProvider.System,
            NullLogger<ReleaseSearchQueryExecutor>.Instance);
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
            result.Single().Releases.Select(release => release.IdentityKeys.Single().Value),
            Is.EquivalentTo(
            [
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            ]));
    }
}
