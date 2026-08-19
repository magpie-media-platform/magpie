using System;
using System.Collections.Generic;
using System.Linq;
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
    public async Task SearchAsync_FansOutToEverySearcher_WhoseContentTypesMatch()
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
        IMetadataSearchService subject = new MetadataSearchService(
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
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].ExternalId, Is.EqualTo("550"));
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
        IMetadataSearchService subject = new MetadataSearchService(
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
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results.Select(r => r.ProviderType), Is.EquivalentTo(["provider-a", "provider-d"]));
        }
    }

    [Test]
    public async Task SearchAsync_InterleavesTiedResults_AcrossSearchers()
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
        IMetadataSearchService subject = new MetadataSearchService(
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
            results.Select(result => result.ExternalId),
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
        IMetadataSearchService subject = new MetadataSearchService(
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
        Assert.That(results, Is.Empty);
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
        IMetadataSearchService subject = new MetadataSearchService(
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
            Assert.That(blankQuery, Is.Empty);
            Assert.That(blankContentType, Is.Empty);
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
    public async Task SearchAsync_PropagatesCancellation_FromAnySlowSearcher()
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
        IMetadataSearchService subject = new MetadataSearchService(
            [],
            [fast.Object, slow.Object],
            registry,
            Mock.Of<IContentHierarchyItemRepository>(),
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<MetadataSearchService>(),
            _cacheFactory);

        using var cts = new CancellationTokenSource();
        var task = Task.Run(() => subject.SearchAsync("Fight Club", "type-a", cancellationToken: cts.Token));

        // Act
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Assert
        Exception? caught = null;
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.That(caught, Is.InstanceOf<OperationCanceledException>());
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
        IMetadataSearchService subject = new MetadataSearchService(
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
    public async Task SearchAsync_FansOutAcrossEveryRootLevel_ForAMultiRootContentType()
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
                    Title = "Some Scene"
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
        IMetadataSearchService subject = new MetadataSearchService(
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
    public async Task SearchAsync_PreservesDistinctItems_AcrossProvidersAndExternalIds()
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
        IMetadataSearchService subject = new MetadataSearchService(
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
        Assert.That(results.Select(r => r.ProviderType), Is.EquivalentTo(["provider-a", "provider-d"]));
    }

    [Test]
    public async Task SearchAsync_DoesNotCollapse_ResultsWithNoExternalId()
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
                    Title = "Some Scene"
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
        IMetadataSearchService subject = new MetadataSearchService(
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
        Assert.That(results, Has.Count.EqualTo(3));
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
        IMetadataSearchService subject = new MetadataSearchService(
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
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Level, Is.EqualTo("type-c-root"));
        }
    }

    [Test]
    public async Task SearchAsync_PreservesProviderReportedLevelSlug_OverSearchedLevel()
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
                    Title = "Some Scene"
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
        IMetadataSearchService subject = new MetadataSearchService(
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
            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(results.Select(r => r.Level), Is.All.EqualTo("type-e-root-b"));
        }
    }

    [Test]
    public async Task SearchAsync_ResolvesTheSingleRoot_ForASingleRootContentType()
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
        IMetadataSearchService subject = new MetadataSearchService(
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
    public async Task SearchAsync_PassesResolvedPage_ToEverySearcher(int requestedPage, int expectedPage)
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
        IMetadataSearchService subject = new MetadataSearchService(
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
    public async Task SearchCandidatesAsync_ReturnsHasMore_WhenProviderReportsMoreAfterShortMatchingPage()
    {
        // Arrange
        var candidate = new MetadataSearchCandidate
        {
            ProviderType = "provider-a",
            ExternalIdSource = "source-a",
            ExternalId = "scene-1",
            ContentType = "type-a",
            Level = "level-a",
            Title = "Scene 1",
            SubjectReferences =
            [
                new ProviderMetadataSearchSubjectReference
                {
                    SubjectKind = "person",
                    DisplayName = "Nora Fenley",
                    RelationshipKind = "performer"
                }
            ]
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
                    candidate with { ExternalId = "scene-2", Title = "Scene 2" },
                    candidate with { ExternalId = "scene-3", Title = "Scene 3" },
                    candidate with { ExternalId = "scene-4", Title = "Scene 4" },
                    candidate with { ExternalId = "scene-5", Title = "Scene 5" },
                    candidate with { ExternalId = "scene-6", Title = "Scene 6" }
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
                        DisplayName = "Nora Fenley",
                        RelationshipKind = "performer"
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
        IMetadataSearchService subject = new MetadataSearchService(
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
                                                                Value = "nora fenley"
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
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_ReturnsCandidateAsInLibrary_WhenMatchingItemIsOnSecondRepositoryPage()
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
        IMetadataSearchService subject = new MetadataSearchService(
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
    public async Task SearchCandidatesAsync_WarnsThatNoProviderIsAvailable_WhenNoSearcherSupportsTheContentType()
    {
        // Arrange
        var searcher = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        searcher.SetupGet(provider => provider.ProviderType).Returns("provider-b");
        searcher.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-b"]);
        IMetadataSearchService subject = new MetadataSearchService(
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
            Assert.That(
                result.Warnings.Single(),
                Is.EqualTo(
                    "No metadata provider is available to run this search. " +
                    "Enable a provider for this content and search again."));
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_ReturnsEveryDistinctExternalId_WhenTwoProvidersReturnEquivalentContent()
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
        IMetadataSearchService subject = new MetadataSearchService(
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
            Assert.That(result.Matches, Has.Count.EqualTo(3));
            Assert.That(result.PageItemCount, Is.EqualTo(3));
            Assert.That(
                result.Matches.Select(match => match.Candidate.ExternalId),
                Is.EquivalentTo(["603", "Q83495", "Q3111633"]));
            Assert.That(
                result.Matches.Select(match => match.Candidate.ProviderType),
                Is.EquivalentTo(["provider-a", "provider-b", "provider-b"]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task PreviewByExternalIdAsync_ReturnsPopulatedPreview_WithTrailersAndCast()
    {
        // Arrange
        IMetadataSearchService subject = new MetadataSearchService(
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
    public async Task PreviewByExternalIdAsync_RequestsRelationshipsOnce_FromTheContributingProvider()
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
        IMetadataSearchService subject = new MetadataSearchService(
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
        IMetadataSearchService subject = new MetadataSearchService(
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
        IMetadataSearchService subject = new MetadataSearchService(
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
        IMetadataSearchService subject = new MetadataSearchService(
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
    public async Task PreviewByExternalIdAsync_HandlesProviderThrow_WithoutRethrow()
    {
        // Arrange
        _provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upstream blew up"));
        IMetadataSearchService subject = new MetadataSearchService(
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
        IMetadataSearchService subject = new MetadataSearchService(
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
    public async Task PreviewByExternalIdAsync_DegradesToNull_ForUnknownContentType()
    {
        // Arrange
        IMetadataSearchService subject = new MetadataSearchService(
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
}
