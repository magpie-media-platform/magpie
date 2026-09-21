using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Caching;
using Magpie.Core.Content;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Models.Paging;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class MetadataSearchServiceTests
{
    private MemoryCacheFactory _cacheFactory = null!;
    private MemoryCacheStore _cacheStore = null!;
    private ILoggerFactory _loggerFactory = null!;
    private IContentTypeRegistry _registry = null!;
    private Mock<IContentHierarchyMetadataRelationshipProvider> _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _cacheStore = new MemoryCacheStore(TimeProvider.System);
        _cacheFactory = new MemoryCacheFactory(_cacheStore);
        _loggerFactory = NullLoggerFactory.Instance;

        var contribution = new MetadataContribution
        {
            ProviderType = "provider-a",
            Title = "Fight Club",
            Overview = "A ticking-time-bomb insomniac.",
            PublishDate = new DateTime(1999, 10, 15, 0, 0, 0, DateTimeKind.Utc),
            Duration = TimeSpan.FromMinutes(139),
            Language = "en",
        };
        contribution.Genres.Add(new Genre { Name = "Drama", Ordinal = 0 });
        contribution.Genres.Add(new Genre { Name = "Thriller", Ordinal = 1 });
        contribution.ContentRatings.Add(new ContentRating { Body = "MPAA", Rating = "R", Ordinal = 0 });
        contribution.Images.Add(new Image { Type = "poster", Url = new Uri("https://img/poster"), Ordinal = 0 });
        contribution.Images.Add(new Image { Type = "backdrop", Url = new Uri("https://img/backdrop"), Ordinal = 1 });
        contribution.Previews.Add(new Preview
        {
            Kind = "trailer",
            Url = new Uri("https://youtu.be/abc"),
            Title = "Official Trailer",
            Ordinal = 0,
        });
        contribution.ExternalIds.Add(new ExternalId { Source = "provider-a.type-a", Value = "550", Ordinal = 0 });

        var actorSeed = new MetadataContribution { ProviderType = "provider-a", Title = "Brad Pitt" };
        actorSeed.Images.Add(new Image { Type = "profile", Url = new Uri("https://img/brad"), Ordinal = 0 });
        var directorSeed = new MetadataContribution { ProviderType = "provider-a", Title = "David Fincher" };
        var relationships = new List<SubjectRelationshipDefinition> {
            new() {
                RelationshipKind = "actor",
                RelationshipQualifier = "Tyler Durden",
                RelationshipOrdinal = 0,
                SubjectKind = "person",
                SubjectName = "Brad Pitt",
                SubjectExternalIds = new Dictionary<string, string> { ["provider-a.person"] = "287" },
                InitialSubjectContribution = actorSeed,
            },
            new() {
                RelationshipKind = "director",
                RelationshipOrdinal = 1,
                SubjectKind = "person",
                SubjectName = "David Fincher",
                InitialSubjectContribution = directorSeed,
            },
        };

        _provider = new Mock<IContentHierarchyMetadataRelationshipProvider>();
        _provider.SetupGet(p => p.ProviderType).Returns("provider-a");
        _provider.SetupGet(p => p.SupportedContentTypes).Returns(["type-a"]);
        _provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult { Contribution = contribution });
        _provider
            .Setup(p => p.GetRelationshipsAsync(
                It.IsAny<MetadataRelationshipRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(relationships);

        var movieDefinition = Mock.Of<IContentTypeDefinitionProvider>(p => p.Definition == new ContentTypeDefinition
        {
            Slug = "type-a",
            DisplayName = "Type A",
            LevelDefinitions =
            new[]
            {
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-a",
                    ParentSlug = null,
                    DisplayName = "Type A",
                    CustomMetadataFields = new List<CustomMetadataDefinition>(),
                },
            },
        });
        _registry = new ContentTypeRegistry([movieDefinition], _loggerFactory.CreateLogger<ContentTypeRegistry>());
    }

    [TearDown]
    public void TearDown()
    {
        _cacheFactory.Dispose();
        _cacheStore.Dispose();
    }

    [Test]
    public async Task SearchAsync_FansOutToEverySearcher_WhenProvidersSupportTheContentType()
    {
        // Arrange
        var movieSearcher = new Mock<IContentHierarchyMetadataSearcher>();
        movieSearcher.SetupGet(s => s.ProviderType).Returns("provider-a");
        movieSearcher.SetupGet(s => s.SupportedContentTypes).Returns(["type-a", "type-c"]);
        movieSearcher
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "550",
                    ContentType = "type-a",
                    Title = "Fight Club"
                }
            ]);

        var tvOnlySearcher = new Mock<IContentHierarchyMetadataSearcher>();
        tvOnlySearcher.SetupGet(s => s.ProviderType).Returns("provider-d");
        tvOnlySearcher.SetupGet(s => s.SupportedContentTypes).Returns(["type-c"]);
        tvOnlySearcher
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-d",
                    ExternalIdSource = "provider-d.kind",
                    ExternalId = "9999",
                    ContentType = "type-c",
                    Title = "Should not appear"
                }
            ]);

        var registry = new ContentTypeRegistry([], NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [movieSearcher.Object, tvOnlySearcher.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var results = await subject.SearchAsync("Fight Club", "type-a", cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Matches, Has.Count.EqualTo(1));
            Assert.That(results.Matches[0].ExternalId, Is.EqualTo("550"));
        }

        movieSearcher.Verify(
            s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        tvOnlySearcher.Verify(
            s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "provider-d only handles 'type-c'; must not be called for 'type-a'.");
    }

    [Test]
    public async Task SearchAsync_MergesResults_WhenMultipleSearchersReturnMatches()
    {
        // Arrange
        var providerA = new Mock<IContentHierarchyMetadataSearcher>();
        providerA.SetupGet(s => s.ProviderType).Returns("provider-a");
        providerA.SetupGet(s => s.SupportedContentTypes).Returns(["type-c"]);
        providerA
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-c",
                    ExternalId = "1396",
                    ContentType = "type-c",
                    Title = "Breaking Bad"
                }
            ]);

        var providerD = new Mock<IContentHierarchyMetadataSearcher>();
        providerD.SetupGet(s => s.ProviderType).Returns("provider-d");
        providerD.SetupGet(s => s.SupportedContentTypes).Returns(["type-c"]);
        providerD
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-d",
                    ExternalIdSource = "provider-d.kind",
                    ExternalId = "81189",
                    ContentType = "type-c",
                    Title = "Breaking Bad Provider D"
                }
            ]);

        var registry = new ContentTypeRegistry([], NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [providerA.Object, providerD.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var results = await subject.SearchAsync("Breaking Bad", "type-c", cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Matches, Has.Count.EqualTo(2));
            Assert.That(results.Matches.Select(r => r.ProviderType), Is.EquivalentTo(["provider-a", "provider-d"]));
        }
    }

    [Test]
    public async Task SearchAsync_InterleavesTiedResults_WhenMultipleSearchersExist()
    {
        // Arrange
        var providerA = new Mock<IContentHierarchyMetadataSearcher>();
        providerA.SetupGet(s => s.ProviderType).Returns("provider-a");
        providerA.SetupGet(s => s.SupportedContentTypes).Returns(["type-c"]);
        providerA
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-c",
                    ExternalId = "a1",
                    ContentType = "type-c",
                    Title = "Zeta"
                },
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-c",
                    ExternalId = "a2",
                    ContentType = "type-c",
                    Title = "Yankee"
                }
            ]);

        var providerD = new Mock<IContentHierarchyMetadataSearcher>();
        providerD.SetupGet(s => s.ProviderType).Returns("provider-d");
        providerD.SetupGet(s => s.SupportedContentTypes).Returns(["type-c"]);
        providerD
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-d",
                    ExternalIdSource = "provider-d.kind",
                    ExternalId = "b1",
                    ContentType = "type-c",
                    Title = "Xray"
                },
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-d",
                    ExternalIdSource = "provider-d.kind",
                    ExternalId = "b2",
                    ContentType = "type-c",
                    Title = "Whiskey"
                }
            ]);

        var registry = new ContentTypeRegistry([], NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [providerA.Object, providerD.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var results = await subject.SearchAsync("anything", "type-c", cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(
            results.Matches.Select(result => result.ExternalId),
            Is.EqualTo(["a1", "b1", "a2", "b2"]));
    }

    [Test]
    public async Task SearchAsync_ReturnsEmpty_WhenNoSearcherSupportsContentType()
    {
        // Arrange
        var tvOnly = new Mock<IContentHierarchyMetadataSearcher>();
        tvOnly.SetupGet(s => s.ProviderType).Returns("provider-d");
        tvOnly.SetupGet(s => s.SupportedContentTypes).Returns(["type-c"]);
        tvOnly
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-d",
                    ExternalIdSource = "provider-d.kind",
                    ExternalId = "1",
                    ContentType = "type-c",
                    Title = "Should not appear"
                }
            ]);

        var registry = new ContentTypeRegistry([], NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [tvOnly.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var results = await subject.SearchAsync("anything", "type-b", cancellationToken: default).ConfigureAwait(false);

        // Assert
        Assert.That(results.Matches, Is.Empty);
        tvOnly.Verify(
            s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task SearchAsync_ReturnsEmpty_WhenQueryOrContentTypeIsBlank()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataSearcher>();
        searcher.SetupGet(s => s.ProviderType).Returns("provider-a");
        searcher.SetupGet(s => s.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "1",
                    ContentType = "type-a",
                    Title = "Anything"
                }
            ]);

        var registry = new ContentTypeRegistry([], NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var blankQuery = await subject.SearchAsync("   ", "type-a", cancellationToken: default).ConfigureAwait(false);
        var blankContentType = await subject.SearchAsync("Inception", "   ", cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(blankQuery.Matches, Is.Empty);
            Assert.That(blankContentType.Matches, Is.Empty);
        }

        searcher.Verify(
            s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task SearchAsync_PropagatesCancellation_WhenASlowSearcherIsCancelled()
    {
        // Arrange
        var fast = new Mock<IContentHierarchyMetadataSearcher>();
        fast.SetupGet(s => s.ProviderType).Returns("provider-a");
        fast.SetupGet(s => s.SupportedContentTypes).Returns(["type-a"]);
        fast
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "550",
                    ContentType = "type-a",
                    Title = "Fight Club"
                }
            ]);

        var slow = new Mock<IContentHierarchyMetadataSearcher>();
        slow.SetupGet(s => s.ProviderType).Returns("provider-d");
        slow.SetupGet(s => s.SupportedContentTypes).Returns(["type-a"]);
        slow
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (
                ContentHierarchyLevelDefinition
                _,
                string __,
                string ___,
                int ____,
                CancellationToken cancellationToken) =>
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                    return [];
                });

        var registry = new ContentTypeRegistry([], NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [fast.Object, slow.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        // Act
        var caught = Assert.CatchAsync<OperationCanceledException>(
            () => subject.SearchAsync("Fight Club", "type-a", cancellationToken: cts.Token));

        // Assert
        Assert.That(caught, Is.Not.Null);
    }

    [Test]
    public async Task SearchAsync_ReturnsTheAnsweringProviderResults_WhenAnotherSearcherNeverAnswers()
    {
        // Arrange
        var answering = new Mock<IContentHierarchyMetadataSearcher>();
        answering.SetupGet(s => s.ProviderType).Returns("provider-a");
        answering.SetupGet(s => s.SupportedContentTypes).Returns(["type-a"]);
        answering
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "550",
                    ContentType = "type-a",
                    Title = "Fight Club"
                }
            ]);

        var silent = new Mock<IContentHierarchyMetadataSearcher>();
        silent.SetupGet(s => s.ProviderType).Returns("provider-d");
        silent.SetupGet(s => s.SupportedContentTypes).Returns(["type-a"]);
        silent
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<IEnumerable<MetadataSearchCandidate>>().Task);

        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var registry = new ContentTypeRegistry([], NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [answering.Object, silent.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            timeProvider,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var pending = subject.SearchAsync("Fight Club", "type-a", cancellationToken: default);
        timeProvider.Advance(TimeSpan.FromSeconds(46));
        var results = await pending.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Matches, Has.Count.EqualTo(1));
            Assert.That(results.Matches[0].ProviderType, Is.EqualTo("provider-a"));
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsEmpty_WhenNoSearcherAnswers()
    {
        // Arrange
        var first = new Mock<IContentHierarchyMetadataSearcher>();
        first.SetupGet(s => s.ProviderType).Returns("provider-a");
        first.SetupGet(s => s.SupportedContentTypes).Returns(["type-a"]);
        first
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<IEnumerable<MetadataSearchCandidate>>().Task);

        var second = new Mock<IContentHierarchyMetadataSearcher>();
        second.SetupGet(s => s.ProviderType).Returns("provider-d");
        second.SetupGet(s => s.SupportedContentTypes).Returns(["type-a"]);
        second
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<IEnumerable<MetadataSearchCandidate>>().Task);

        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var registry = new ContentTypeRegistry([], NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [first.Object, second.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            timeProvider,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var pending = subject.SearchAsync("Jackass 3D 2010", "type-a", cancellationToken: default);
        timeProvider.Advance(TimeSpan.FromSeconds(46));
        var results = await pending.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Matches, Is.Empty);
            Assert.That(
                results.Warnings,
                Is.EquivalentTo([
                    "provider-a did not answer the search in time. Try again.",
                    "provider-d did not answer the search in time. Try again."
                ]));
        }
    }

    [Test]
    public void SearchAsync_PropagatesCancellation_WhenTheCallerCancelsAndTheSearcherIgnoresTheToken()
    {
        // Arrange
        var silent = new Mock<IContentHierarchyMetadataSearcher>();
        silent.SetupGet(s => s.ProviderType).Returns("provider-a");
        silent.SetupGet(s => s.SupportedContentTypes).Returns(["type-a"]);
        silent
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<IEnumerable<MetadataSearchCandidate>>().Task);

        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var registry = new ContentTypeRegistry([], NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [silent.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            timeProvider,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        using var cancellation = new CancellationTokenSource();

        // Act
        var pending = subject.SearchAsync("Fight Club", "type-a", cancellationToken: cancellation.Token);
        cancellation.Cancel();

        // Assert
        Assert.That(async () => await pending.ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task SearchAsync_UsesContentTypeRootLevelSlug_WhenDefinitionProviderIsRegistered()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataSearcher>();
        searcher.SetupGet(s => s.ProviderType).Returns("provider-a");
        searcher.SetupGet(s => s.SupportedContentTypes).Returns(["type-c"]);
        searcher
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-c",
                    ExternalId = "1396",
                    ContentType = "type-c",
                    Title = "Breaking Bad"
                }
            ]);

        var definition = new ContentTypeDefinition
        {
            Slug = "type-c",
            DisplayName = "Type C",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-c-root",
                    ParentSlug = null,
                    DisplayName = "Type C Root",
                    CustomMetadataFields = []
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-c-child",
                    ParentSlug = "type-c-root",
                    DisplayName = "Type C Child",
                    CustomMetadataFields = []
                }
            ]
        };

        var registry = new ContentTypeRegistry(
            [Mock.Of<IContentTypeDefinitionProvider>(p => p.Definition == definition)],
            NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        await subject.SearchAsync("Breaking Bad", "type-c", cancellationToken: default).ConfigureAwait(false);

        // Assert
        searcher.Verify(
            s => s.SearchAsync(
                It.Is<ContentHierarchyLevelDefinition>(l => l.Slug == "type-c-root"),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Search must pass the root-level slug ('type-c-root') for the 'type-c' content type.");
    }

    [Test]
    public async Task SearchAsync_FansOutAcrossEveryRootLevel_WhenAMultiRootContentTypeIsRequested()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataSearcher>();
        searcher.SetupGet(s => s.ProviderType).Returns("provider-b");
        searcher.SetupGet(s => s.SupportedContentTypes).Returns(["type-e"]);
        searcher
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-b",
                    ExternalIdSource = "provider-b.kind",
                    ExternalId = "1",
                    ContentType = "type-e",
                    Title = "Some Item"
                }
            ]);

        var definition = new ContentTypeDefinition
        {
            Slug = "type-e",
            DisplayName = "Type E",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-e-root-a", ParentSlug = null, DisplayName = "Root A", CustomMetadataFields = [],
                    Subfolder = "root-a"
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-e-root-b", ParentSlug = null, DisplayName = "Root B", CustomMetadataFields = [],
                    Subfolder = "root-b"
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-e-root-c", ParentSlug = null, DisplayName = "Root C", CustomMetadataFields = [],
                    Subfolder = "root-c"
                }
            ]
        };

        var registry = new ContentTypeRegistry(
            [Mock.Of<IContentTypeDefinitionProvider>(p => p.Definition == definition)],
            NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        await subject.SearchAsync("anything", "type-e", cancellationToken: default).ConfigureAwait(false);

        // Assert
        searcher.Verify(
            s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3),
            "Search must invoke the searcher once per root level of a multi-root content type.");
        foreach (var slug in new[] { "type-e-root-a", "type-e-root-b", "type-e-root-c" })
        {
            searcher.Verify(
                s => s.SearchAsync(
                    It.Is<ContentHierarchyLevelDefinition>(l => l.Slug == slug),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task SearchAsync_PreservesDistinctItems_WhenMultipleProvidersAndExternalIdsExist()
    {
        // Arrange
        var providerA = new Mock<IContentHierarchyMetadataSearcher>();
        providerA.SetupGet(s => s.ProviderType).Returns("provider-a");
        providerA.SetupGet(s => s.SupportedContentTypes).Returns(["type-c"]);
        providerA
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-c",
                    ExternalId = "1396",
                    ContentType = "type-c",
                    Title = "Breaking Bad"
                }
            ]);

        var providerD = new Mock<IContentHierarchyMetadataSearcher>();
        providerD.SetupGet(s => s.ProviderType).Returns("provider-d");
        providerD.SetupGet(s => s.SupportedContentTypes).Returns(["type-c"]);
        providerD
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-d",
                    ExternalIdSource = "provider-d.kind",
                    ExternalId = "81189",
                    ContentType = "type-c",
                    Title = "Breaking Bad Provider D"
                }
            ]);

        var registry = new ContentTypeRegistry([], NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [providerA.Object, providerD.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var results = await subject.SearchAsync("Breaking Bad", "type-c", cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(results.Matches.Select(r => r.ProviderType), Is.EquivalentTo(["provider-a", "provider-d"]));
    }

    [Test]
    public async Task SearchAsync_KeepsBothCandidates_WhenTitlesMatchAndNeitherProviderReportsAYear()
    {
        // Arrange
        var providerE = new Mock<IContentHierarchyMetadataSearcher>();
        providerE.SetupGet(s => s.ProviderType).Returns("provider-e");
        providerE.SetupGet(s => s.SupportedContentTypes).Returns(["type-y"]);
        providerE
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-e",
                    ExternalIdSource = "provider-e.kind",
                    ExternalId = "1",
                    ContentType = "type-y",
                    Title = "Nirvana"
                }
            ]);

        var providerF = new Mock<IContentHierarchyMetadataSearcher>();
        providerF.SetupGet(s => s.ProviderType).Returns("provider-f");
        providerF.SetupGet(s => s.SupportedContentTypes).Returns(["type-y"]);
        providerF
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-f",
                    ExternalIdSource = "provider-f.kind",
                    ExternalId = "2",
                    ContentType = "type-y",
                    Title = "Nirvana"
                }
            ]);

        var registry = new ContentTypeRegistry([], NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [providerE.Object, providerF.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var results = await subject.SearchAsync("Nirvana", "type-y", cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(results.Matches.Select(r => r.ProviderType), Is.EquivalentTo(["provider-e", "provider-f"]));
    }

    [Test]
    public async Task SearchAsync_DoesNotCollapse_WhenResultsHaveNoExternalId()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataSearcher>();
        searcher.SetupGet(s => s.ProviderType).Returns("provider-b");
        searcher.SetupGet(s => s.SupportedContentTypes).Returns(["type-e"]);
        searcher
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-b",
                    ExternalIdSource = "provider-b.kind",
                    ExternalId = "1",
                    ContentType = "type-e",
                    Title = "Some Item"
                }
            ]);

        var definition = new ContentTypeDefinition
        {
            Slug = "type-e",
            DisplayName = "Type E",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-e-root-a", ParentSlug = null, DisplayName = "Root A", CustomMetadataFields = [],
                    Subfolder = "root-a"
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-e-root-b", ParentSlug = null, DisplayName = "Root B", CustomMetadataFields = [],
                    Subfolder = "root-b"
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-e-root-c", ParentSlug = null, DisplayName = "Root C", CustomMetadataFields = [],
                    Subfolder = "root-c"
                }
            ]
        };

        var registry = new ContentTypeRegistry(
            [Mock.Of<IContentTypeDefinitionProvider>(p => p.Definition == definition)],
            NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var results = await subject.SearchAsync("anything", "type-e", cancellationToken: default).ConfigureAwait(false);

        // Assert
        Assert.That(results.Matches, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task SearchAsync_StampsSearchedLevelSlug_WhenProviderReportsNone()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataSearcher>();
        searcher.SetupGet(s => s.ProviderType).Returns("provider-a");
        searcher.SetupGet(s => s.SupportedContentTypes).Returns(["type-c"]);
        searcher
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-c",
                    ExternalId = "1396",
                    ContentType = "type-c",
                    Title = "Breaking Bad"
                }
            ]);

        var definition = new ContentTypeDefinition
        {
            Slug = "type-c",
            DisplayName = "Type C",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-c-root", ParentSlug = null, DisplayName = "Type C Root", CustomMetadataFields = []
                }
            ]
        };

        var registry = new ContentTypeRegistry(
            [Mock.Of<IContentTypeDefinitionProvider>(p => p.Definition == definition)],
            NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var results = await subject.SearchAsync("Breaking Bad", "type-c", cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Matches, Has.Count.EqualTo(1));
            Assert.That(results.Matches[0].Level, Is.EqualTo("type-c-root"));
        }
    }

    [Test]
    public async Task SearchAsync_PreservesProviderReportedLevelSlug_WhenProviderAndSearchedLevelsDiffer()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataSearcher>();
        searcher.SetupGet(s => s.ProviderType).Returns("provider-b");
        searcher.SetupGet(s => s.SupportedContentTypes).Returns(["type-e"]);
        searcher
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-b",
                    ExternalIdSource = "provider-b.kind",
                    ExternalId = "1",
                    ContentType = "type-e",
                    Level = "type-e-root-b",
                    Title = "Some Item"
                }
            ]);

        var definition = new ContentTypeDefinition
        {
            Slug = "type-e",
            DisplayName = "Type E",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-e-root-a", ParentSlug = null, DisplayName = "Root A", CustomMetadataFields = [],
                    Subfolder = "root-a"
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-e-root-b", ParentSlug = null, DisplayName = "Root B", CustomMetadataFields = [],
                    Subfolder = "root-b"
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-e-root-c", ParentSlug = null, DisplayName = "Root C", CustomMetadataFields = [],
                    Subfolder = "root-c"
                }
            ]
        };

        var registry = new ContentTypeRegistry(
            [Mock.Of<IContentTypeDefinitionProvider>(p => p.Definition == definition)],
            NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var results = await subject.SearchAsync("anything", "type-e", cancellationToken: default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Matches, Has.Count.EqualTo(1));
            Assert.That(results.Matches[0].Level, Is.EqualTo("type-e-root-b"));
        }
    }

    [Test]
    public async Task SearchAsync_ResolvesTheSingleRoot_WhenASingleRootContentTypeIsRequested()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataSearcher>();
        searcher.SetupGet(s => s.ProviderType).Returns("provider-a");
        searcher.SetupGet(s => s.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "550",
                    ContentType = "type-a",
                    Title = "Fight Club"
                }
            ]);

        var definition = new ContentTypeDefinition
        {
            Slug = "type-a",
            DisplayName = "Type A",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-a", ParentSlug = null, DisplayName = "Type A", CustomMetadataFields = []
                }
            ]
        };

        var registry = new ContentTypeRegistry(
            [Mock.Of<IContentTypeDefinitionProvider>(p => p.Definition == definition)],
            NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        await subject.SearchAsync("Fight Club", "type-a", cancellationToken: default).ConfigureAwait(false);

        // Assert
        searcher.Verify(
            s => s.SearchAsync(
                It.Is<ContentHierarchyLevelDefinition>(l => l.Slug == "type-a"),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCase(3, 3, TestName = "SearchAsync_PassesRequestedPage_ToEverySearcher")]
    [TestCase(0, 1, TestName = "SearchAsync_ClampsNonPositivePage_ToOne")]
    public async Task SearchAsync_PassesResolvedPage_WhenPagingIsRequested(int requestedPage, int expectedPage)
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataSearcher>();
        searcher.SetupGet(s => s.ProviderType).Returns("provider-a");
        searcher.SetupGet(s => s.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "550",
                    ContentType = "type-a",
                    Title = "Fight Club"
                }
            ]);

        var registry = new ContentTypeRegistry([], NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        await subject.SearchAsync("Fight Club", "type-a", page: requestedPage, default).ConfigureAwait(false);

        // Assert
        searcher.Verify(
            s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                expectedPage,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task SearchCandidatesAsync_ReturnsSearchHitsWithoutHydration_WhenTitleIsOnlyMetadataCriterion()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                25,
                string.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a",
                        ExternalIdSource = "source-a",
                        ExternalId = "snow-white",
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = "Snow White and the Seven Dwarfs",
                        Year = 1937
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 1,
                TotalItems = 0
            });
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            _registry,
            hierarchyItems.Object,
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        },
                        Metadata = new CriteriaSet<MetadataCriteria>
                        {
                            And =
                            [
                                new MetadataCriteria
                                {
                                    Title = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And =
                                        [
                                            new PropertyCriterion<string>
                                            {
                                                Operator = CriteriaValueOperator.ContainsString,
                                                Value = "snow white"
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Matches[0].Candidate.Title, Is.EqualTo("Snow White and the Seven Dwarfs"));
            searcher.Verify(
                provider => provider.HydrateAsync(
                    It.IsAny<MetadataSearchCandidate>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_ReturnsHasMore_WhenProviderReportsMoreAfterShortMatchingPage()
    {
        // Arrange
        var candidate = new MetadataSearchCandidate
        {
            ProviderType = "provider-a",
            ExternalIdSource = "source-a",
            ExternalId = "item-1",
            ContentType = "type-a",
            Level = "level-a",
            Title = "Item 1",
        };
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                25,
                string.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    candidate,
                    candidate with { ExternalId = "item-2", Title = "Item 2" },
                    candidate with { ExternalId = "item-3", Title = "Item 3" },
                    candidate with { ExternalId = "item-4", Title = "Item 4" },
                    candidate with { ExternalId = "item-5", Title = "Item 5" },
                    candidate with { ExternalId = "item-6", Title = "Item 6" }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = true }
            });
        searcher
            .Setup(provider => provider.HydrateAsync(
                It.IsAny<MetadataSearchCandidate>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataSearchCandidate value, string _, CancellationToken _) => value with
            {
                SubjectReferences =
                [
                    new ProviderMetadataSearchSubjectReference
                    {
                        SubjectKind = "person",
                        DisplayName = "Alex Carter",
                        RelationshipKind = "director"
                    }
                ]
            });

        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 6,
                TotalItems = 0
            });
        var definition = new ContentTypeDefinition
        {
            Slug = "type-a",
            DisplayName = "Type A",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "level-a",
                    DisplayName = "Level A",
                    CustomMetadataFields = []
                }
            ]
        };
        var registry = new ContentTypeRegistry(
            [Mock.Of<IContentTypeDefinitionProvider>(provider => provider.Definition == definition)],
            NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            registry,
            hierarchyItems.Object,
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        },
                        Level = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "level-a" }]
                        },
                        Metadata = new CriteriaSet<MetadataCriteria>
                        {
                            And =
                            [
                                new MetadataCriteria
                                {
                                    Relationships = new CollectionCriteria<RelationshipCriteria>
                                    {
                                        Some =
                                        [
                                            [
                                                new RelationshipCriteria
                                                {
                                                    Subject = new CriteriaSet<PropertyCriterion<string>>
                                                    {
                                                        And =
                                                        [
                                                            new PropertyCriterion<string>
                                                            {
                                                                Operator = CriteriaValueOperator.ContainsString,
                                                                Value = "alex carter"
                                                            }
                                                        ]
                                                    },
                                                    Type = new CriteriaSet<PropertyCriterion<string>>
                                                    {
                                                        And = [new PropertyCriterion<string> { Value = "person" }]
                                                    }
                                                }
                                            ]
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.SearchCandidatesAsync(
            criteria,
            "type-a",
            page: 1,
            cancellationToken: default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(6));
            Assert.That(result.Matches.Select(match => match.ExistingContentHierarchyItemId), Has.All.Null);
            Assert.That(result.PageItemCount, Is.EqualTo(6));
            Assert.That(result.PageInfo.RequestedPage, Is.EqualTo(1));
            Assert.That(result.PageInfo.HasMore, Is.True);
            searcher.Verify(
                provider => provider.HydrateAsync(
                    It.IsAny<MetadataSearchCandidate>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(6));
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_ReturnsCandidateAsInLibrary_WhenTheMatchIsOnALaterPage()
    {
        // Arrange
        var existingContentHierarchyItemId = Guid.Parse("3db97837-8a29-4b92-937b-13f948bfd62d");
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();

        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                25,
                string.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a",
                        ExternalIdSource = "source-a",
                        ExternalId = "shared-id",
                        ContentType = "type-a",
                        Title = "Owned title"
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        searcher
            .Setup(provider => provider.HydrateAsync(
                It.IsAny<MetadataSearchCandidate>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataSearchCandidate candidate, string _, CancellationToken _) => candidate);

        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .SetupSequence(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        ContentType = "type-a",
                        Level = "root",
                        Slug = "wrong-source",
                        Name = "Wrong source",
                        MetadataContainer = new MetadataContainer
                        {
                            Contributions =
                            {
                                new MetadataContribution
                                {
                                    ProviderType = "provider-a",
                                    ExternalIds =
                                    {
                                        new ExternalId { Source = "source-b", Value = "shared-id" }
                                    }
                                }
                            }
                        }
                    }
                ],
                PageNumber = 1,
                PageSize = 1,
                TotalItems = 2
            })
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = existingContentHierarchyItemId,
                        ContentType = "type-a",
                        Level = "root",
                        Slug = "matching-source",
                        Name = "Matching source",
                        MetadataContainer = new MetadataContainer
                        {
                            Contributions =
                            {
                                new MetadataContribution
                                {
                                    ProviderType = "provider-a",
                                    ExternalIds =
                                    {
                                        new ExternalId { Source = "source-a", Value = "shared-id" }
                                    }
                                }
                            }
                        }
                    }
                ],
                PageNumber = 2,
                PageSize = 1,
                TotalItems = 2
            })
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                PageNumber = 3,
                PageSize = 1,
                TotalItems = 2
            });
        var registry = new ContentTypeRegistry(
            [],
            NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            registry,
            hierarchyItems.Object,
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var result = await subject.SearchCandidatesAsync(
            new ContentFilterCriteria(),
            "type-a",
            page: 1,
            cancellationToken: default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Matches[0].InLibrary, Is.True);
            Assert.That(result.Matches[0].ExistingContentHierarchyItemId, Is.EqualTo(existingContentHierarchyItemId));
            Assert.That(result.ExistsInLibraryCount, Is.EqualTo(1));
            hierarchyItems.Verify(
                repository => repository.QueryAsync(
                    It.IsAny<ContentHierarchyItemCriteria>(),
                    It.Is<PagingParameter>(paging => paging.PageNumber == 2 && paging.PageSize == 1),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_ReturnsCandidateOutsideLibrary_WhenMatchingItemIsAChild()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();

        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                25,
                string.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a",
                        ExternalIdSource = "source-a",
                        ExternalId = "shared-id",
                        ContentType = "type-a",
                        Title = "Unowned title"
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        searcher
            .Setup(provider => provider.HydrateAsync(
                It.IsAny<MetadataSearchCandidate>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataSearchCandidate candidate, string _, CancellationToken _) => candidate);

        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();

        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = Guid.Parse("45b43b4b-5bb3-412e-935d-0766236b597c"),
                        ParentId = Guid.Parse("e60a1596-1b25-4a3e-beb0-35a666a3261f"),
                        ContentType = "type-a",
                        Level = "child",
                        Slug = "matching-child",
                        Name = "Matching child",
                        MetadataContainer = new MetadataContainer
                        {
                            Contributions =
                            {
                                new MetadataContribution
                                {
                                    ProviderType = "provider-a",
                                    ExternalIds =
                                    {
                                        new ExternalId { Source = "source-a", Value = "shared-id" }
                                    }
                                }
                            }
                        }
                    }
                ],
                PageNumber = 1,
                PageSize = 1,
                TotalItems = 1
            });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria =>
                    criteria.RootLevelOnly == true &&
                    criteria.Metadata.And.Single().ExternalIds.Some.Single().Single().Source.And.Single().Value ==
                    "source-a" &&
                    criteria.Metadata.And.Single().ExternalIds.Some.Single().Single().Value.And.Single().Value ==
                    "shared-id"),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 1,
                TotalItems = 0
            });
        var registry = new ContentTypeRegistry(
            [],
            NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());

        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            registry,
            hierarchyItems.Object,
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var result = await subject.SearchCandidatesAsync(
            new ContentFilterCriteria(),
            "type-a",
            page: 1,
            cancellationToken: default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Matches[0].InLibrary, Is.False);
            Assert.That(result.Matches[0].ExistingContentHierarchyItemId, Is.Null);
            Assert.That(result.ExistsInLibraryCount, Is.Zero);
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_SearchesEveryRequestedType_WhenTheCriterionListsMoreThanOne()
    {
        // Arrange
        var typeB = Mock.Of<IContentTypeDefinitionProvider>(p => p.Definition == new ContentTypeDefinition
        {
            Slug = "type-b",
            DisplayName = "Type B",
            LevelDefinitions =
            new[]
            {
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-b",
                    ParentSlug = null,
                    DisplayName = "Type B",
                    CustomMetadataFields = new List<CustomMetadataDefinition>(),
                },
            },
        });
        var typeA = Mock.Of<IContentTypeDefinitionProvider>(p => p.Definition == new ContentTypeDefinition
        {
            Slug = "type-a",
            DisplayName = "Type A",
            LevelDefinitions =
            new[]
            {
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-a",
                    ParentSlug = null,
                    DisplayName = "Type A",
                    CustomMetadataFields = new List<CustomMetadataDefinition>(),
                },
            },
        });
        var registry = new ContentTypeRegistry(
            [typeA, typeB],
            _loggerFactory.CreateLogger<ContentTypeRegistry>());
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a", "type-b"]);
        searcher
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                25,
                string.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a",
                        ExternalIdSource = "source-a",
                        ExternalId = "a-1",
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = "Snow White"
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        searcher
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-b",
                It.IsAny<ContentHierarchyItemCriteria>(),
                25,
                string.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a",
                        ExternalIdSource = "source-b",
                        ExternalId = "b-1",
                        ContentType = "type-b",
                        Level = "type-b",
                        Title = "Snow White"
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            registry,
            hierarchyItems.Object,
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.In,
                                    Values = ["type-a", "type-b"]
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.SearchCandidatesAsync(criteria, null, page: 1, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Matches.Select(match => match.Candidate.ExternalId),
                Is.EquivalentTo(["a-1", "b-1"]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_ReturnsQuietEmpty_WhenNoSearcherSupportsTheContentType()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-b");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-b"]);
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            _registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_WarnsAndReturnsNoMatches_WhenTheSearcherNeverAnswers()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(provider => provider.AdvancedSearchAsync(
                It.IsAny<string>(),
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<MetadataSearchPage>().Task);

        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            _registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            timeProvider,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        }
                    }
                ]
            }
        };

        // Act
        var pending = subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default);
        timeProvider.Advance(TimeSpan.FromSeconds(46));
        var result = await pending.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Is.Empty);
            Assert.That(
                result.Warnings.Single(),
                Is.EqualTo("provider-a did not answer the search in time. Try again."));
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_KeepsFastProviderResults_WhenAnotherProviderTimesOut()
    {
        // Arrange
        var fastSearcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        fastSearcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        fastSearcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        fastSearcher
            .Setup(provider => provider.AdvancedSearchAsync(
                It.IsAny<string>(),
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a",
                        ExternalIdSource = "source-a",
                        ExternalId = "item-1",
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = "Item 1"
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        fastSearcher
            .Setup(provider => provider.HydrateAsync(
                It.IsAny<MetadataSearchCandidate>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataSearchCandidate value, string _, CancellationToken _) => value);

        var slowSearcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        slowSearcher.SetupGet(provider => provider.ProviderType).Returns("provider-b");
        slowSearcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        slowSearcher
            .Setup(provider => provider.AdvancedSearchAsync(
                It.IsAny<string>(),
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<MetadataSearchPage>().Task);

        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 1,
                TotalItems = 0
            });
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var subject = new MetadataSearchService(
            [],
            [fastSearcher.Object, slowSearcher.Object],
            _registry,
            hierarchyItems.Object,
            timeProvider,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        }
                    }
                ]
            }
        };

        // Act
        var pending = subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default);
        timeProvider.Advance(TimeSpan.FromSeconds(46));
        var result = await pending.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Matches[0].Candidate.Title, Is.EqualTo("Item 1"));
            Assert.That(
                result.Warnings.Single(),
                Is.EqualTo("provider-b did not answer the search in time. Try again."));
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_ReturnsQuietEmpty_WhenProviderReportsNotSupported()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(provider => provider.AdvancedSearchAsync(
                It.IsAny<string>(),
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MetadataSearchPage.NotSupported);
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            _registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_RetriesTheProvider_WhenTheCachedPageExpires()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .SetupSequence(provider => provider.AdvancedSearchAsync(
                It.IsAny<string>(),
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches = [],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            })
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a",
                        ExternalIdSource = "source-a",
                        ExternalId = "item-1",
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = "Item 1"
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        searcher
            .Setup(provider => provider.HydrateAsync(
                It.IsAny<MetadataSearchCandidate>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataSearchCandidate value, string _, CancellationToken _) => value);

        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 1,
                TotalItems = 0
            });
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var cacheStore = new MemoryCacheStore(timeProvider);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            _registry,
            hierarchyItems.Object,
            timeProvider,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        }
                    }
                ]
            }
        };

        // Act
        var first = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);
        timeProvider.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        var second = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Matches, Is.Empty);
            Assert.That(second.Matches, Has.Count.EqualTo(1));
            Assert.That(second.Matches[0].Candidate.Title, Is.EqualTo("Item 1"));
            Assert.That(second.Warnings, Is.Empty);
        }

        searcher.Verify(
            provider => provider.AdvancedSearchAsync(
                It.IsAny<string>(),
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task SearchCandidatesAsync_SkipsTheProviderWithAWarning_WhenThreeSearchesFailedInARow()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(provider => provider.AdvancedSearchAsync(
                It.IsAny<string>(),
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("upstream unavailable"));
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            _registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        }
                    }
                ]
            }
        };

        // Act
        var first = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);
        var second = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);
        var third = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);
        var fourth = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                first.Warnings.Single(),
                Is.EqualTo("provider-a could not finish the search. Try again."));
            Assert.That(
                second.Warnings.Single(),
                Is.EqualTo("provider-a could not finish the search. Try again."));
            Assert.That(
                third.Warnings.Single(),
                Is.EqualTo("provider-a could not finish the search. Try again."));
            Assert.That(fourth.Matches, Is.Empty);
            Assert.That(fourth.Warnings, Has.Some.Contains("skipped after repeated failed searches"));
        }

        searcher.Verify(
            provider => provider.AdvancedSearchAsync(
                It.IsAny<string>(),
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Test]
    public async Task SearchCandidatesAsync_ProbesTheSkippedProvider_WhenTheFailureCooldownExpires()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(provider => provider.AdvancedSearchAsync(
                It.IsAny<string>(),
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("upstream unavailable"));
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var cacheStore = new MemoryCacheStore(timeProvider);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            _registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            timeProvider,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        }
                    }
                ]
            }
        };

        // Act
        await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);
        await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);
        await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);
        var skipped = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);
        timeProvider.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        var probed = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(skipped.Warnings, Has.Some.Contains("skipped after repeated failed searches"));
            Assert.That(
                probed.Warnings.Single(),
                Is.EqualTo("provider-a could not finish the search. Try again."));
        }

        searcher.Verify(
            provider => provider.AdvancedSearchAsync(
                It.IsAny<string>(),
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(4));
    }

    [Test]
    public async Task SearchCandidatesAsync_KeepsTheSearchedCandidate_WhenHydrationNeverAnswers()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(provider => provider.AdvancedSearchAsync(
                It.IsAny<string>(),
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a",
                        ExternalIdSource = "source-a",
                        ExternalId = "item-1",
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = "Item 1"
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        searcher
            .Setup(provider => provider.HydrateAsync(
                It.IsAny<MetadataSearchCandidate>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<MetadataSearchCandidate?>().Task);

        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 1,
                TotalItems = 0
            });
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            _registry,
            hierarchyItems.Object,
            timeProvider,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        }
                    }
                ]
            }
        };

        // Act
        var pending = subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default);
        timeProvider.Advance(TimeSpan.FromSeconds(46));
        var result = await pending.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Matches[0].Candidate.Title, Is.EqualTo("Item 1"));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_CollapsesToOneResult_WhenTwoProvidersReturnTheSameItem()
    {
        // Arrange
        var first = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        first.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        first.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        first
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a",
                        ExternalIdSource = "provider-a",
                        ExternalId = "603",
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = "The Matrix",
                        Year = 1999
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        var second = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        second.SetupGet(provider => provider.ProviderType).Returns("provider-b");
        second.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        second
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-b",
                        ExternalIdSource = "provider-b",
                        ExternalId = "Q83495",
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = "The Matrix",
                        Year = 1999,
                        Overview = "A hacker learns the truth.",
                        ThumbnailUrl = new Uri("https://img/matrix")
                    },
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-b",
                        ExternalIdSource = "provider-b",
                        ExternalId = "Q3111633",
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = "The Matrix Reloaded",
                        Year = 2003
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 2,
                TotalItems = 0
            });
        var subject = new MetadataSearchService(
            [],
            [first.Object, second.Object],
            _registry,
            hierarchyItems.Object,
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        },
                        Metadata = new CriteriaSet<MetadataCriteria>
                        {
                            And =
                            [
                                new MetadataCriteria
                                {
                                    Title = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And =
                                        [
                                            new PropertyCriterion<string>
                                            {
                                                Operator = CriteriaValueOperator.ContainsString,
                                                Value = "The Matrix"
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(2));
            Assert.That(result.PageItemCount, Is.EqualTo(2));
            Assert.That(
                result.Matches.Select(match => match.Candidate.ExternalId),
                Is.EquivalentTo(["603", "Q3111633"]));
            Assert.That(
                result.Matches.Select(match => match.Candidate.ProviderType),
                Is.EquivalentTo(["provider-a", "provider-b"]));
            var survivor = result.Matches.Single(match => match.Candidate.ExternalId == "603");
            Assert.That(
                survivor.Candidate.AdditionalExternalIds.Select(id => (id.Source, id.Value)),
                Is.EquivalentTo([("provider-b", "Q83495")]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_CollapsesToOneResult_WhenASecondProviderCarriesTheFirstProvidersId()
    {
        // Arrange
        var first = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        first.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        first.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        first
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a",
                        ExternalIdSource = "provider-a",
                        ExternalId = "603",
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = "127 Hours",
                        Year = 2010
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        var second = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        second.SetupGet(provider => provider.ProviderType).Returns("provider-b");
        second.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        second
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-b",
                        ExternalIdSource = "provider-b",
                        ExternalId = "Q83495",
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = "One Hundred Twenty-Seven Hours",
                        Year = 1899,
                        AdditionalExternalIds =
                        [
                            new MetadataSearchCandidateExternalId { Source = "provider-a", Value = "603" }
                        ]
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 2,
                TotalItems = 0
            });
        var subject = new MetadataSearchService(
            [],
            [first.Object, second.Object],
            _registry,
            hierarchyItems.Object,
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Matches[0].Candidate.ExternalId, Is.EqualTo("603"));
            Assert.That(result.Matches[0].Candidate.ProviderType, Is.EqualTo("provider-a"));
            Assert.That(result.Matches[0].Candidate.Title, Is.EqualTo("127 Hours"));
            Assert.That(
                result.Matches[0].Candidate.AdditionalExternalIds.Select(id => (id.Source, id.Value)),
                Is.EquivalentTo([("provider-b", "Q83495")]));
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_KeepsBothResults_WhenTitlesMatchButYearsDiffer()
    {
        // Arrange
        var first = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        first.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        first.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        first
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a",
                        ExternalIdSource = "provider-a",
                        ExternalId = "1",
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = "Dune",
                        Year = 1984
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        var second = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        second.SetupGet(provider => provider.ProviderType).Returns("provider-b");
        second.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        second
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-b",
                        ExternalIdSource = "provider-b",
                        ExternalId = "2",
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = "Dune",
                        Year = 2021
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 2,
                TotalItems = 0
            });
        var subject = new MetadataSearchService(
            [],
            [first.Object, second.Object],
            _registry,
            hierarchyItems.Object,
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(2));
            Assert.That(
                result.Matches.Select(match => match.Candidate.ExternalId),
                Is.EquivalentTo(["1", "2"]));
        }
    }

    [Test]
    public async Task PreviewByExternalIdAsync_ReturnsPopulatedPreview_WhenTrailersAndCastAreReturned()
    {
        // Arrange
        var subject = new MetadataSearchService(
            [_provider.Object],
            [],
            _registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var preview = await subject.PreviewByExternalIdAsync(
            "provider-a", "provider-a.type-a", "550", "type-a", levelSlug: null, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(preview, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(preview!.ProviderType, Is.EqualTo("provider-a"));
            Assert.That(preview.ContentType, Is.EqualTo("type-a"));
            Assert.That(preview.LevelSlug, Is.EqualTo("type-a"));
            Assert.That(preview.Title, Is.EqualTo("Fight Club"));
            Assert.That(preview.Overview, Is.EqualTo("A ticking-time-bomb insomniac."));
            Assert.That(preview.Year, Is.EqualTo(1999));
            Assert.That(preview.ReleaseDate, Is.Not.Null);
            Assert.That(preview.Duration, Is.EqualTo(TimeSpan.FromMinutes(139)));
            Assert.That(preview.Language, Is.EqualTo("en"));

            Assert.That(preview.Genres, Is.EquivalentTo(["Drama", "Thriller"]));
            Assert.That(preview.ContentRatings.Single().Body, Is.EqualTo("MPAA"));
            Assert.That(preview.ContentRatings.Single().Rating, Is.EqualTo("R"));

            Assert.That(preview.Images, Has.Count.EqualTo(2));
            Assert.That(preview.Images.Any(i => i.Type == "poster"), Is.True);

            Assert.That(preview.Previews, Has.Count.EqualTo(1));
            Assert.That(preview.Previews.Single().Kind, Is.EqualTo("trailer"));
            Assert.That(preview.Previews.Single().Url, Is.EqualTo("https://youtu.be/abc"));

            Assert.That(preview.ExternalIds.Any(e => e.Source == "provider-a.type-a" && e.Value == "550"), Is.True);

            Assert.That(preview.RelatedSubjects, Has.Count.EqualTo(2));
            var actor = preview.RelatedSubjects.First(r => r.RelationshipKind == "actor");
            Assert.That(actor.Name, Is.EqualTo("Brad Pitt"));
            Assert.That(actor.Kind, Is.EqualTo("person"));
            Assert.That(actor.RelationshipQualifier, Is.EqualTo("Tyler Durden"));
            Assert.That(actor.ImageUrl, Is.EqualTo("https://img/brad"));
            var director = preview.RelatedSubjects.First(r => r.RelationshipKind == "director");
            Assert.That(director.Name, Is.EqualTo("David Fincher"));
        }
    }

    [Test]
    public async Task PreviewByExternalIdAsync_RequestsRelationshipsOnce_WhenPreviewIncludesRelationships()
    {
        // Arrange
        var contribution = new MetadataContribution
        {
            ProviderType = "provider-a",
            Title = "Fight Club",
        };
        var provider = new Mock<IContentHierarchyMetadataRelationshipProvider>();
        provider.SetupGet(p => p.ProviderType).Returns("provider-a");
        provider.SetupGet(p => p.SupportedContentTypes).Returns(["type-a"]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult { Contribution = contribution });
        provider
            .Setup(p => p.GetRelationshipsAsync(
                It.IsAny<MetadataRelationshipRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "actor",
                    RelationshipOrdinal = 0,
                    SubjectKind = "person",
                    SubjectName = "Brad Pitt",
                },
            ]);
        var subject = new MetadataSearchService(
            [provider.Object],
            [],
            _registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var preview = await subject.PreviewByExternalIdAsync(
            "provider-a", "provider-a.type-a", "550", "type-a", levelSlug: null, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(preview, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(preview!.RelatedSubjects, Has.Count.EqualTo(1));
            Assert.That(preview.RelatedSubjects.Single().Name, Is.EqualTo("Brad Pitt"));
        }

        provider.Verify(
            p => p.GetRelationshipsAsync(
                It.IsAny<MetadataRelationshipRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task PreviewByExternalIdAsync_DoesNotPersist_WhenPreviewSucceeds()
    {
        // Arrange
        var metadataContainerRepository = new Mock<IMetadataContainerRepository>();
        var subject = new MetadataSearchService(
            [_provider.Object],
            [],
            _registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var preview = await subject.PreviewByExternalIdAsync(
            "provider-a", "provider-a.type-a", "550", "type-a", levelSlug: null, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(preview, Is.Not.Null);
        metadataContainerRepository.VerifyNoOtherCalls();
    }

    [Test]
    public async Task PreviewByExternalIdAsync_ReturnsNull_WhenNoProviderResolvesTheId()
    {
        // Arrange
        var subject = new MetadataSearchService(
            [
                Mock.Of<IContentHierarchyMetadataProvider>(p =>
                    p.ProviderType == "provider-a" && p.SupportedContentTypes == new[] { "type-a" }),
            ],
            [],
            _registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var preview = await subject.PreviewByExternalIdAsync(
            "provider-a", "provider-a.type-a", "does-not-exist", "type-a", levelSlug: null, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(preview, Is.Null);
    }

    [Test]
    public async Task PreviewByExternalIdAsync_ReturnsNull_WhenNoProviderSupportsContentType()
    {
        // Arrange
        var subject = new MetadataSearchService(
            [],
            [],
            _registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var preview = await subject.PreviewByExternalIdAsync(
            "provider-a", "provider-a.type-a", "550", "type-a", levelSlug: null, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(preview, Is.Null);
    }

    [Test]
    public async Task PreviewByExternalIdAsync_HandlesProviderThrow_WhenTheProviderThrows()
    {
        // Arrange
        _provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upstream blew up"));
        var subject = new MetadataSearchService(
            [_provider.Object],
            [],
            _registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        ContentPreview? preview = null;
        Assert.DoesNotThrowAsync(async () =>
        {
            preview = await subject.PreviewByExternalIdAsync(
                "provider-a", "provider-a.type-a", "550", "type-a", levelSlug: null, default)
                .ConfigureAwait(false);
        });

        // Assert
        Assert.That(preview, Is.Null);
    }

    [Test]
    public async Task PreviewByExternalIdAsync_ReturnsPartial_WhenRelatedSubjectExpansionThrows()
    {
        // Arrange
        _provider
            .Setup(p => p.GetRelationshipsAsync(
                It.IsAny<MetadataRelationshipRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cast fetch blew up"));
        var subject = new MetadataSearchService(
            [_provider.Object],
            [],
            _registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var preview = await subject.PreviewByExternalIdAsync(
            "provider-a", "provider-a.type-a", "550", "type-a", levelSlug: null, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(preview, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(preview!.Title, Is.EqualTo("Fight Club"));
            Assert.That(preview.RelatedSubjects, Is.Empty);
        }
    }

    [Test]
    public async Task PreviewByExternalIdAsync_DegradesToNull_WhenUnknownContentTypeIsRequested()
    {
        // Arrange
        var subject = new MetadataSearchService(
            [_provider.Object],
            [],
            _registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var preview = await subject.PreviewByExternalIdAsync(
            "provider-a", "provider-a.type-a", "550", "does-not-exist", levelSlug: null, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(preview, Is.Null);
    }

    [Test]
    public async Task SearchCandidatesAsync_HydratesAndRejects_WhenTheLanguageCriterionNeedsDetail()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                25,
                string.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a",
                        ExternalIdSource = "source-a",
                        ExternalId = "snow-white",
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = "Snow White and the Seven Dwarfs",
                        Year = 1937
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        searcher
            .Setup(provider => provider.HydrateAsync(
                It.IsAny<MetadataSearchCandidate>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataSearchCandidate candidate, string _, CancellationToken _) =>
                candidate with { Language = "en" });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 1,
                TotalItems = 0
            });
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            _registry,
            hierarchyItems.Object,
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        },
                        Metadata = new CriteriaSet<MetadataCriteria>
                        {
                            And =
                            [
                                new MetadataCriteria
                                {
                                    Language = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And = [new PropertyCriterion<string> { Value = "ja" }]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Is.Empty);
            searcher.Verify(
                provider => provider.HydrateAsync(
                    It.IsAny<MetadataSearchCandidate>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsTheOtherProvidersResults_WhenOneSearcherThrows()
    {
        // Arrange
        var failingSearcher = new Mock<IContentHierarchyMetadataSearcher>();
        failingSearcher.SetupGet(s => s.ProviderType).Returns("provider-b");
        failingSearcher.SetupGet(s => s.SupportedContentTypes).Returns(["type-a"]);
        failingSearcher
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("502 Bad Gateway"));
        var healthySearcher = new Mock<IContentHierarchyMetadataSearcher>();
        healthySearcher.SetupGet(s => s.ProviderType).Returns("provider-a");
        healthySearcher.SetupGet(s => s.SupportedContentTypes).Returns(["type-a"]);
        healthySearcher
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "550",
                    ContentType = "type-a",
                    Title = "Fight Club"
                }
            ]);
        var registry = new ContentTypeRegistry([], NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [failingSearcher.Object, healthySearcher.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var results = await subject.SearchAsync("Fight Club", "type-a", cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Matches, Has.Count.EqualTo(1));
            Assert.That(results.Matches[0].ExternalId, Is.EqualTo("550"));
            Assert.That(
                results.Warnings,
                Is.EqualTo(["provider-b could not finish the search. Try again."]));
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsOtherResultsAndAWarning_WhenOneSearcherTimesOut()
    {
        // Arrange
        var stalledSearcher = new Mock<IContentHierarchyMetadataSearcher>();
        stalledSearcher.SetupGet(s => s.ProviderType).Returns("provider-b");
        stalledSearcher.SetupGet(s => s.SupportedContentTypes).Returns(["type-a"]);
        stalledSearcher
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient" +
                ".Timeout of 20 seconds elapsing."));
        var healthySearcher = new Mock<IContentHierarchyMetadataSearcher>();
        healthySearcher.SetupGet(s => s.ProviderType).Returns("provider-a");
        healthySearcher.SetupGet(s => s.SupportedContentTypes).Returns(["type-a"]);
        healthySearcher
            .Setup(s => s.SearchAsync(
                It.IsAny<ContentHierarchyLevelDefinition>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "550",
                    ContentType = "type-a",
                    Title = "Fight Club"
                }
            ]);
        var registry = new ContentTypeRegistry([], NullLoggerFactory.Instance.CreateLogger<ContentTypeRegistry>());
        var subject = new MetadataSearchService(
            [],
            [stalledSearcher.Object, healthySearcher.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        // Act
        var results = await subject.SearchAsync("Fight Club", "type-a", cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Matches, Has.Count.EqualTo(1));
            Assert.That(results.Matches[0].ExternalId, Is.EqualTo("550"));
            Assert.That(
                results.Warnings,
                Is.EqualTo(["provider-b could not finish the search. Try again."]));
        }
    }

    [TestCase(1, "c1", "c31")]
    [TestCase(2, "c32", "c62")]
    public async Task SearchCandidatesAsync_FillsThePage_WhenTheHydratedCriterionRejectsSome(
        int page,
        string expectedFirst,
        string expectedLast)
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                string.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                string _,
                ContentHierarchyItemCriteria _,
                int maximumResults,
                string _,
                CancellationToken _) => new MetadataSearchPage
                {
                    Supported = true,
                    Matches =
                    [
                        .. Enumerable.Range(1, Math.Min(maximumResults, 200)).Select(index =>
                        new MetadataSearchCandidate
                        {
                            ProviderType = "provider-a",
                            ExternalIdSource = "source-a",
                            ExternalId = $"c{index}",
                            ContentType = "type-a",
                            Level = "type-a",
                            Title = $"Candidate {index}"
                        })
                    ],
                    PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = maximumResults < 200 }
                });
        searcher
            .Setup(provider => provider.HydrateAsync(
                It.IsAny<MetadataSearchCandidate>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataSearchCandidate candidate, string _, CancellationToken _) =>
                candidate with
                {
                    Language = int.Parse(candidate.ExternalId[1..], CultureInfo.InvariantCulture) % 5 == 0 ? "en" : "ja"
                });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 1,
                TotalItems = 0
            });
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            _registry,
            hierarchyItems.Object,
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        },
                        Metadata = new CriteriaSet<MetadataCriteria>
                        {
                            And =
                            [
                                new MetadataCriteria
                                {
                                    Language = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And = [new PropertyCriterion<string> { Value = "ja" }]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.SearchCandidatesAsync(criteria, "type-a", page: page, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(25));
            Assert.That(result.Matches[0].Candidate.ExternalId, Is.EqualTo(expectedFirst));
            Assert.That(result.Matches[24].Candidate.ExternalId, Is.EqualTo(expectedLast));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }
    }

    [TestCase(1, "c1", "c2")]
    [TestCase(2, "c30", "c26")]
    public async Task SearchCandidatesAsync_CutsThePageFromProviderOrder_WhenATitleQueryIsPresent(
        int page,
        string expectedFirst,
        string expectedSecond)
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                string.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                string _,
                ContentHierarchyItemCriteria _,
                int maximumResults,
                string _,
                CancellationToken _) => new MetadataSearchPage
                {
                    Supported = true,
                    Matches =
                    [
                        .. Enumerable.Range(1, Math.Min(maximumResults, 60)).Select(index =>
                        new MetadataSearchCandidate
                        {
                            ProviderType = "provider-a",
                            ExternalIdSource = "source-a",
                            ExternalId = $"c{index}",
                            ContentType = "type-a",
                            Level = "type-a",
                            Title = index == 30 ? "Jane Example" : $"Jane Example item {index}"
                        })
                    ],
                    PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = maximumResults < 60 }
                });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 1,
                TotalItems = 0
            });
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            _registry,
            hierarchyItems.Object,
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        },
                        Metadata = new CriteriaSet<MetadataCriteria>
                        {
                            And =
                            [
                                new MetadataCriteria
                                {
                                    Title = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And =
                                        [
                                            new PropertyCriterion<string>
                                            {
                                                Operator = CriteriaValueOperator.ContainsString,
                                                Value = "jane example"
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.SearchCandidatesAsync(criteria, "type-a", page: page, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(25));
            Assert.That(result.Matches[0].Candidate.ExternalId, Is.EqualTo(expectedFirst));
            Assert.That(result.Matches[1].Candidate.ExternalId, Is.EqualTo(expectedSecond));
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_ShowsARowVerifiedLate_WhenTheEarlierPageWasShort()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher.Setup(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ContentHierarchyItemCriteria _, int maximumResults,
                string _, CancellationToken _) => new MetadataSearchPage
                {
                    Supported = true,
                    Matches = [.. Enumerable.Range(1, maximumResults / 25).Select(index => new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a", ExternalIdSource = "source-a", ExternalId = $"c{index}",
                        ContentType = "type-a", Level = "type-a", Title = $"Candidate {index}"
                    })],
                    PageInfo = new() { RequestedPage = 1, HasMore = true }
                });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        using var cacheStore = new MemoryCacheStore(TimeProvider.System);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new MetadataSearchService(
            [], [searcher.Object], _registry, hierarchyItems.Object,
            TimeProvider.System, NullLogger<MetadataSearchService>.Instance, cacheFactory);

        // Act
        var first = await subject.SearchCandidatesAsync(new(), "type-a", 1).ConfigureAwait(false);
        var second = await subject.SearchCandidatesAsync(new(), "type-a", 2).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Matches.Select(match => match.Candidate.ExternalId),
                Is.EqualTo(["c1", "c2", "c3", "c4", "c5"]));
            Assert.That(first.PageInfo.HasMore, Is.True);
            Assert.That(second.Matches.Select(match => match.Candidate.ExternalId),
                Is.EqualTo(["c6", "c7", "c8", "c9"]));
            Assert.That(second.PageInfo.HasMore, Is.True);
            searcher.Verify(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()), Times.Exactly(9));
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_KeepsPagesDisjoint_WhenTheUserIdlesBetweenPages()
    {
        // Arrange
        var searcherA = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcherA.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcherA.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcherA.Setup(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ContentHierarchyItemCriteria _, int maximumResults,
                string _, CancellationToken _) => new MetadataSearchPage
                {
                    Supported = true,
                    Matches = [.. Enumerable.Range(1, maximumResults * 2 / 5).Select(index =>
                        new MetadataSearchCandidate
                        {
                            ProviderType = "provider-a", ExternalIdSource = "source-a", ExternalId = $"a{index}",
                            ContentType = "type-a", Level = "type-a", Title = $"Candidate a{index}"
                        })],
                    PageInfo = new() { RequestedPage = 1, HasMore = true }
                });
        var searcherB = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcherB.SetupGet(provider => provider.ProviderType).Returns("provider-b");
        searcherB.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcherB.Setup(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ContentHierarchyItemCriteria _, int maximumResults,
                string _, CancellationToken _) => new MetadataSearchPage
                {
                    Supported = true,
                    Matches = [.. Enumerable.Range(1, maximumResults * 3 / 5).Select(index =>
                        new MetadataSearchCandidate
                        {
                            ProviderType = "provider-b", ExternalIdSource = "source-b", ExternalId = $"b{index}",
                            ContentType = "type-a", Level = "type-a", Title = $"Candidate b{index}"
                        })],
                    PageInfo = new() { RequestedPage = 1, HasMore = true }
                });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        var timeProvider = new FakeTimeProvider();
        using var cacheStore = new MemoryCacheStore(timeProvider);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new MetadataSearchService(
            [], [searcherA.Object, searcherB.Object], _registry, hierarchyItems.Object,
            timeProvider, NullLogger<MetadataSearchService>.Instance, cacheFactory);

        // Act
        var first = await subject.SearchCandidatesAsync(new(), "type-a", 1).ConfigureAwait(false);
        timeProvider.Advance(TimeSpan.FromHours(1));
        var second = await subject.SearchCandidatesAsync(new(), "type-a", 2).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Matches[0].Candidate.ExternalId, Is.EqualTo("a1"));
            Assert.That(second.Matches[0].Candidate.ExternalId, Is.EqualTo("a11"));
            Assert.That(second.Matches, Has.Count.EqualTo(25));
            Assert.That(first.Matches.Select(match => match.Candidate.ExternalId)
                .Intersect(second.Matches.Select(match => match.Candidate.ExternalId)), Is.Empty);
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_ReturnsWarning_WhenProviderReportsPartialFailure()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher.Setup(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches = [],
                PageInfo = new() { RequestedPage = 1, HasMore = false },
                Warnings = ["The remote request failed."]
            });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        var subject = new MetadataSearchService(
            [], [searcher.Object], _registry, hierarchyItems.Object,
            TimeProvider.System, NullLogger<MetadataSearchService>.Instance, _cacheFactory);

        // Act
        var result = await subject.SearchCandidatesAsync(new(), "type-a", 1).ConfigureAwait(false);

        // Assert
        Assert.That(result.Warnings, Is.EqualTo(["provider-a: The remote request failed."]));
    }

    [Test]
    public async Task SearchCandidatesAsync_AsksAFailedProviderOnce_WhenALaterPageIsRequestedCold()
    {
        // Arrange
        var searcherA = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcherA.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcherA.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcherA.Setup(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("502 Bad Gateway"));
        var searcherB = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcherB.SetupGet(provider => provider.ProviderType).Returns("provider-b");
        searcherB.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcherB.Setup(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ContentHierarchyItemCriteria _, int maximumResults,
                string _, CancellationToken _) => new MetadataSearchPage
                {
                    Supported = true,
                    Matches = [.. Enumerable.Range(1, maximumResults).Select(index => new MetadataSearchCandidate
                    {
                        ProviderType = "provider-b", ExternalIdSource = "source-b", ExternalId = $"b{index}",
                        ContentType = "type-a", Level = "type-a", Title = $"Candidate b{index}"
                    })],
                    PageInfo = new() { RequestedPage = 1, HasMore = true }
                });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        using var cacheStore = new MemoryCacheStore(TimeProvider.System);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new MetadataSearchService(
            [], [searcherA.Object, searcherB.Object], _registry, hierarchyItems.Object,
            TimeProvider.System, NullLogger<MetadataSearchService>.Instance, cacheFactory);

        // Act
        var result = await subject.SearchCandidatesAsync(new(), "type-a", 3).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.Candidate.ExternalId),
                Is.EqualTo([
                    "b51", "b52", "b53", "b54", "b55", "b56", "b57", "b58", "b59", "b60",
                    "b61", "b62", "b63", "b64", "b65", "b66", "b67", "b68", "b69", "b70",
                    "b71", "b72", "b73", "b74", "b75"
                ]));
            Assert.That(result.Warnings, Is.EqualTo(["provider-a could not finish the search. Try again."]));
            searcherA.Verify(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_RecomputesAPagePastTheEnd_WhenTheProviderGrows()
    {
        // Arrange
        var rows = 10;
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher.Setup(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ContentHierarchyItemCriteria _, int maximumResults,
                string _, CancellationToken _) => new MetadataSearchPage
                {
                    Supported = true,
                    Matches = [.. Enumerable.Range(1, Math.Min(rows, maximumResults)).Select(index =>
                        new MetadataSearchCandidate
                        {
                            ProviderType = "provider-a", ExternalIdSource = "source-a", ExternalId = $"c{index}",
                            ContentType = "type-a", Level = "type-a", Title = $"Candidate {index}"
                        })],
                    PageInfo = new() { RequestedPage = 1, HasMore = maximumResults < rows }
                });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        var timeProvider = new FakeTimeProvider();
        using var cacheStore = new MemoryCacheStore(timeProvider);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new MetadataSearchService(
            [], [searcher.Object], _registry, hierarchyItems.Object,
            timeProvider, NullLogger<MetadataSearchService>.Instance, cacheFactory);

        // Act
        var beyond = await subject.SearchCandidatesAsync(new(), "type-a", 3).ConfigureAwait(false);
        rows = 100;
        timeProvider.Advance(TimeSpan.FromSeconds(16));
        await subject.SearchCandidatesAsync(new(), "type-a", 1).ConfigureAwait(false);
        var fourth = await subject.SearchCandidatesAsync(new(), "type-a", 4).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(beyond.Matches, Is.Empty);
            Assert.That(beyond.PageInfo.HasMore, Is.False);
            Assert.That(fourth.Matches.Select(match => match.Candidate.ExternalId),
                Is.EqualTo([
                    "c76", "c77", "c78", "c79", "c80", "c81", "c82", "c83", "c84", "c85",
                    "c86", "c87", "c88", "c89", "c90", "c91", "c92", "c93", "c94", "c95",
                    "c96", "c97", "c98", "c99", "c100"
                ]));
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_ComputesTheEarlierPages_WhenALaterPageIsRequestedFirst()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher.Setup(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ContentHierarchyItemCriteria _, int maximumResults,
                string _, CancellationToken _) => new MetadataSearchPage
                {
                    Supported = true,
                    Matches = [.. Enumerable.Range(1, maximumResults).Select(index => new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a", ExternalIdSource = "source-a", ExternalId = $"c{index}",
                        ContentType = "type-a", Level = "type-a", Title = $"Candidate {index}"
                    })],
                    PageInfo = new() { RequestedPage = 1, HasMore = true }
                });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        var timeProvider = new FakeTimeProvider();
        using var cacheStore = new MemoryCacheStore(timeProvider);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new MetadataSearchService(
            [], [searcher.Object], _registry, hierarchyItems.Object,
            timeProvider, NullLogger<MetadataSearchService>.Instance, cacheFactory);

        // Act
        var cold = await subject.SearchCandidatesAsync(new(), "type-a", 6).ConfigureAwait(false);
        timeProvider.Advance(TimeSpan.FromSeconds(16));
        var expired = await subject.SearchCandidatesAsync(new(), "type-a", 6).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cold.Matches, Has.Count.EqualTo(25));
            Assert.That(cold.Matches[0].Candidate.ExternalId, Is.EqualTo("c126"));
            Assert.That(cold.Matches[^1].Candidate.ExternalId, Is.EqualTo("c150"));
            Assert.That(expired.Matches.Select(match => match.Candidate.ExternalId),
                Is.EqualTo(cold.Matches.Select(match => match.Candidate.ExternalId)));
            searcher.Verify(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), 150,
                string.Empty, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_MergesADuplicateIntoTheProviderRow_WhenItAppearsInALaterWindow()
    {
        // Arrange
        var searcherA = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcherA.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcherA.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcherA.Setup(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ContentHierarchyItemCriteria _, int maximumResults,
                string _, CancellationToken _) =>
                new MetadataSearchPage
                {
                    Supported = true,
                    Matches = [.. Enumerable.Range(1, Math.Min(maximumResults, 50)).Select(index =>
                        new MetadataSearchCandidate
                        {
                            ProviderType = "provider-a", ExternalIdSource = "source-a", ExternalId = $"a{index}",
                            ContentType = "type-a", Level = "type-a", Title = $"Movie a{index}",
                            AdditionalExternalIds = index == 26
                                ? [new() { Source = "imdb", Value = "shared" }]
                                : []
                        })],
                    PageInfo = new() { RequestedPage = 1, HasMore = maximumResults < 50 }
                });
        var searcherB = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcherB.SetupGet(provider => provider.ProviderType).Returns("provider-b");
        searcherB.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcherB.Setup(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches = [new()
                {
                    ProviderType = "provider-b", ExternalIdSource = "source-b", ExternalId = "b1",
                    ContentType = "type-a", Level = "type-a", Title = "Shared movie",
                    AdditionalExternalIds = [new() { Source = "imdb", Value = "shared" }]
                }],
                PageInfo = new() { RequestedPage = 1, HasMore = false }
            });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        var subject = new MetadataSearchService(
            [], [searcherA.Object, searcherB.Object], _registry, hierarchyItems.Object,
            TimeProvider.System, NullLogger<MetadataSearchService>.Instance, _cacheFactory);

        // Act
        var first = await subject.SearchCandidatesAsync(new(), "type-a", 1).ConfigureAwait(false);
        var second = await subject.SearchCandidatesAsync(new(), "type-a", 2).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Matches.Select(match => match.Candidate.ExternalId),
                Is.EqualTo([
                    "a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8", "a9", "a10",
                    "a11", "a12", "a13", "a14", "a15", "a16", "a17", "a18", "a19", "a20",
                    "a21", "a22", "a23", "a24", "a25"
                ]));
            Assert.That(second.Matches.Select(match => match.Candidate.ExternalId),
                Is.EqualTo([
                    "a26", "a27", "a28", "a29", "a30", "a31", "a32", "a33", "a34", "a35",
                    "a36", "a37", "a38", "a39", "a40", "a41", "a42", "a43", "a44", "a45",
                    "a46", "a47", "a48", "a49", "a50"
                ]));
            Assert.That(second.Matches.Select(match => match.Candidate.ExternalId), Does.Not.Contain("b1"));
            Assert.That(second.PageInfo.HasMore, Is.False);
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_AnswersAnEmptyPage_WhenTheRequestedPageIsBeyondTheCeiling()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher.Setup(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches = [],
                PageInfo = new() { RequestedPage = 1, HasMore = true }
            });
        var subject = new MetadataSearchService(
            [], [searcher.Object], _registry, Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System, NullLogger<MetadataSearchService>.Instance, _cacheFactory);

        // Act
        var result = await subject.SearchCandidatesAsync(new(), "type-a", int.MaxValue).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.PageInfo.HasMore, Is.False);
            searcher.Verify(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(false, 5, "a1", "a20")]
    [TestCase(true, 5, "a1", "a20")]
    [TestCase(true, 8, "a1", "a13")]
    public async Task SearchCandidatesAsync_KeepsPagesDisjoint_WhenAnEarlierProviderGrows(
        bool hasMore,
        int divisor,
        string expectedFirst,
        string expectedSecond)
    {
        // Arrange
        var searcherA = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcherA.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcherA.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcherA.Setup(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ContentHierarchyItemCriteria _, int maximumResults,
                string _, CancellationToken _) =>
                new MetadataSearchPage
                {
                    Supported = true,
                    Matches = [.. Enumerable.Range(1, maximumResults * 2 / divisor).Select(index =>
                        new MetadataSearchCandidate
                        {
                            ProviderType = "provider-a", ExternalIdSource = "source-a", ExternalId = $"a{index}",
                            ContentType = "type-a", Level = "type-a",
                            Title = index == 20 ? "Candidate" : $"Candidate a{index}"
                        })],
                    PageInfo = new() { RequestedPage = 1, HasMore = maximumResults == 25 || hasMore }
                });
        var searcherB = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcherB.SetupGet(provider => provider.ProviderType).Returns("provider-b");
        searcherB.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcherB.Setup(provider => provider.AdvancedSearchAsync(
                "type-a", It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(),
                string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ContentHierarchyItemCriteria _, int maximumResults,
                string _, CancellationToken _) =>
                new MetadataSearchPage
                {
                    Supported = true,
                    Matches = [.. Enumerable.Range(1, maximumResults * 3 / 5).Select(index =>
                        new MetadataSearchCandidate
                        {
                            ProviderType = "provider-b", ExternalIdSource = "source-b", ExternalId = $"b{index}",
                            ContentType = "type-a", Level = "type-a", Title = $"Candidate b{index}"
                        })],
                    PageInfo = new() { RequestedPage = 1, HasMore = maximumResults == 25 || hasMore }
                });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        var subject = new MetadataSearchService(
            [], [searcherA.Object, searcherB.Object], _registry, hierarchyItems.Object,
            TimeProvider.System, NullLogger<MetadataSearchService>.Instance, _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And = [new()
                {
                    Metadata = new()
                    {
                        And = [new()
                        {
                            Title = new()
                            {
                                And = [new() { Operator = CriteriaValueOperator.ContainsString, Value = "Candidate" }]
                            }
                        }]
                    }
                }]
            }
        };

        // Act
        var first = await subject.SearchCandidatesAsync(criteria, "type-a", 1).ConfigureAwait(false);
        var second = await subject.SearchCandidatesAsync(criteria, "type-a", 2).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Matches, Has.Count.EqualTo(25));
            Assert.That(second.Matches, Has.Count.EqualTo(25));
            Assert.That(first.Matches[0].Candidate.ExternalId, Is.EqualTo(expectedFirst));
            Assert.That(second.Matches[0].Candidate.ExternalId, Is.EqualTo(expectedSecond));
            Assert.That(first.Matches.Select(match => match.Candidate.ExternalId)
                .Intersect(second.Matches.Select(match => match.Candidate.ExternalId)), Is.Empty);
            Assert.That(second.PageInfo.HasMore, Is.EqualTo(hasMore));
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_KeepsTheRowsAProviderAnswered_WhenItFailsInALaterFillRound()
    {
        // Arrange
        var searcherA = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcherA.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcherA.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcherA
            .SetupSequence(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                string.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches = [.. Enumerable.Range(1, 10).Select(index => new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a",
                        ExternalIdSource = "source-a",
                        ExternalId = "a" + index,
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = "Candidate a" + index
                    })],
                PageInfo = new() { RequestedPage = 1, HasMore = true }
            })
            .ThrowsAsync(new HttpRequestException("502 Bad Gateway"));
        var searcherB = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcherB.SetupGet(provider => provider.ProviderType).Returns("provider-b");
        searcherB.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcherB
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                string.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                string _,
                ContentHierarchyItemCriteria _,
                int maximumResults,
                string _,
                CancellationToken _) =>
                new MetadataSearchPage
                {
                    Supported = true,
                    Matches = [.. Enumerable.Range(1, maximumResults <= 25 ? 10 : 25).Select(index =>
                        new MetadataSearchCandidate
                    {
                        ProviderType = "provider-b",
                        ExternalIdSource = "source-b",
                        ExternalId = "b" + index,
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = "Candidate b" + index
                    })],
                    PageInfo = new() { RequestedPage = 1, HasMore = true }
                });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 1,
                TotalItems = 0
            });
        var subject = new MetadataSearchService(
            [],
            [searcherA.Object, searcherB.Object],
            _registry,
            hierarchyItems.Object,
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(25));
            Assert.That(result.Matches.Select(match => match.Candidate.ExternalId), Does.Contain("a10"));
            Assert.That(result.Matches.Select(match => match.Candidate.ExternalId), Does.Contain("b15"));
            Assert.That(result.Warnings, Is.EqualTo(["provider-a could not finish the search. Try again."]));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_KeepsTheSamePageBoundary_WhenNoProviderHasMore()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        searcher
            .Setup(provider => provider.AdvancedSearchAsync(
                "type-a",
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                string.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    .. Enumerable.Range(1, 40).Select(index => new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a",
                        ExternalIdSource = "source-a",
                        ExternalId = $"c{index}",
                        ContentType = "type-a",
                        Level = "type-a",
                        Title = index == 30 ? "Jane Example" : $"Jane Example item {index}"
                    })
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 1,
                TotalItems = 0
            });
        var subject = new MetadataSearchService(
            [],
            [searcher.Object],
            _registry,
            hierarchyItems.Object,
            TimeProvider.System,
            _loggerFactory.CreateLogger<MetadataSearchService>(),
            _cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        },
                        Metadata = new CriteriaSet<MetadataCriteria>
                        {
                            And =
                            [
                                new MetadataCriteria
                                {
                                    Title = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And =
                                        [
                                            new PropertyCriterion<string>
                                            {
                                                Operator = CriteriaValueOperator.ContainsString,
                                                Value = "jane example"
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(25));
            Assert.That(result.Matches[0].Candidate.ExternalId, Is.EqualTo("c1"));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }
    }
}
