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
using Magpie.Models.Paging;
using Magpie.Models.Releases;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Releases;

[TestFixture]
public sealed class ReleaseEvaluationServiceTests
{
    private Mock<IContentFactProvider> _contentFactProvider = null!;
    private Mock<IContentHierarchyItemRepository> _contentHierarchyItems = null!;
    private IReleaseEvaluationService _subject = null!;
    private HashSet<Guid> _matchingIds = null!;
    private ContentHierarchyItem _show = null!;
    private ContentHierarchyItem _season1 = null!;
    private ContentHierarchyItem _season2 = null!;
    private ContentHierarchyItem _season3 = null!;
    private ContentHierarchyItem _episode1 = null!;
    private ContentHierarchyItem _episode2 = null!;
    private ContentHierarchyItem _episode3 = null!;
    private ContentHierarchyItem _episode4 = null!;
    private ContentHierarchyItem _episode5 = null!;
    private ContentHierarchyItem _episode6 = null!;

    [SetUp]
    public void SetUp()
    {
        _matchingIds = [];
        _show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show",
        };
        _season1 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            Ordinal = 1,
        };
        _season2 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "season-2",
            Name = "Season 2",
            Ordinal = 2,
        };
        _season3 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "season-3",
            Name = "Season 3",
            Ordinal = 3,
        };
        _episode1 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _season1.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-1",
            Name = "Episode 1",
            Ordinal = 1,
        };
        _episode2 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _season1.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-2",
            Name = "Episode 2",
            Ordinal = 2,
        };
        _episode3 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _season1.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-3",
            Name = "Episode 3",
            Ordinal = 3,
        };
        _episode4 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _season2.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-4",
            Name = "Episode 1",
            Ordinal = 1,
        };
        _episode5 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _season2.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-5",
            Name = "Episode 2",
            Ordinal = 2,
        };
        _episode6 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _season3.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-6",
            Name = "Episode 1",
            Ordinal = 1,
        };

        var lineage = new Dictionary<Guid, IReadOnlyList<ContentHierarchyItem>>
        {
            [_show.Id] = [_show],
            [_season1.Id] = [_season1, _show],
            [_season2.Id] = [_season2, _show],
            [_season3.Id] = [_season3, _show],
            [_episode1.Id] = [_episode1, _season1, _show],
            [_episode2.Id] = [_episode2, _season1, _show],
            [_episode3.Id] = [_episode3, _season1, _show],
            [_episode4.Id] = [_episode4, _season2, _show],
            [_episode5.Id] = [_episode5, _season2, _show],
            [_episode6.Id] = [_episode6, _season3, _show],
        };
        var items = lineage.ToDictionary(pair => pair.Key, pair => pair.Value[0]);

        _contentHierarchyItems = new Mock<IContentHierarchyItemRepository>();
        _contentHierarchyItems
            .Setup(value => value.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => lineage[id]);
        _contentHierarchyItems
            .Setup(value => value.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => items.GetValueOrDefault(id));
        _contentHierarchyItems
            .Setup(value => value.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                ContentHierarchyItemCriteria criteria,
                PagingParameter _,
                SortParameter? _,
                CancellationToken _) =>
            {
                var parentId = criteria.ParentId.And.Single().Value;
                var level = criteria.Level.And.Single().Value;
                return new PagedList<ContentHierarchyItem>
                {
                    Items =
                    [.. items.Values.Where(item => item.ParentId == parentId && item.Level == level)],
                };
            });

        var materializedMetadata = new Mock<IMaterializedMetadataRepository>();
        materializedMetadata
            .Setup(value => value.QueryAsync(
                It.IsAny<MetadataCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MaterializedMetadata>());

        var releaseMatchProvider = new Mock<IReleaseMatchContentTypeDefinitionProvider>();
        releaseMatchProvider
            .Setup(value => value.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns((
                ContentTarget target,
                IReadOnlyList<IReadOnlyList<ContentFact>> _,
                Release _,
                IReadOnlyList<ContentFact> _) => _matchingIds.Contains(target.Item.Id)
                    ? ReleaseMatchResult.Confident
                    : ReleaseMatchResult.NoMatch);

        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(value => value.FindProviderFor("tv")).Returns(releaseMatchProvider.Object);
        contentTypeRegistry
            .Setup(value => value.GetChildLevels("tv", "show"))
            .Returns([new ContentHierarchyLevelDefinition
            {
                Slug = "season",
                ParentSlug = "show",
                DisplayName = "Season",
                CustomMetadataFields = [],
                OrdinalKind = OrdinalKind.Number,
            }]);
        contentTypeRegistry
            .Setup(value => value.GetChildLevels("tv", "season"))
            .Returns([new ContentHierarchyLevelDefinition
            {
                Slug = "episode",
                ParentSlug = "season",
                DisplayName = "Episode",
                CustomMetadataFields = [],
                OrdinalKind = OrdinalKind.Number,
            }]);
        contentTypeRegistry.Setup(value => value.GetChildLevels("tv", "episode")).Returns([]);
        _contentFactProvider = new Mock<IContentFactProvider>();

        _subject = new ReleaseEvaluationService(
            _contentHierarchyItems.Object,
            materializedMetadata.Object,
            Mock.Of<IReleaseRepository>(),
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            contentTypeRegistry.Object,
            [_contentFactProvider.Object],
            TimeProvider.System,
            NullLogger<ReleaseEvaluationService>.Instance);
    }

    [Test]
    public async Task CreateEvaluationContextAsync_DoesNotGetProviderFacts_WhenProviderCannotProvideFacts()
    {
        // Arrange
        _contentFactProvider
            .Setup(provider => provider.CanProvideFacts(
                It.Is<ContentTarget>(target => target.Item.Id == _show.Id)))
            .Returns(false);

        // Act
        _ = await _subject.CreateEvaluationContextAsync(_show.Id).ConfigureAwait(false);

        // Assert
        _contentFactProvider.Verify(
            provider => provider.CanProvideFacts(
                It.Is<ContentTarget>(target => target.Item.Id == _show.Id)),
            Times.Once);
        _contentFactProvider.Verify(
            provider => provider.GetFactsAsync(
                It.IsAny<ContentTarget>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ResolveScopeAsync_ReturnsEmpty_WhenSearchedNodeDoesNotMatch()
    {
        // Arrange
        var release = new Release { Protocol = "torrent", Title = "The Court Ordered Smoothie" };

        // Act
        var result = await _subject.ResolveScopeAsync(release, [], _show.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ScopeNodeIds, Is.Empty);
            Assert.That(result.ActionableNodeIds, Is.Empty);
        }

        _contentHierarchyItems.Verify(
            value => value.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria =>
                    criteria.ParentId.And.Any(criterion => criterion.Value == _show.Id)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ResolveScopeAsync_ReturnsOnlyEpisode_WhenSearchedEpisodeMatches()
    {
        // Arrange
        var release = new Release { Protocol = "torrent", Title = "Show.S01E01" };
        IReadOnlyList<ContentFact> releaseFacts = [new ContentFact { Value = "Show" }];
        _matchingIds.Add(_episode1.Id);

        // Act
        var result = await _subject.ResolveScopeAsync(release, releaseFacts, _episode1.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ScopeNodeIds, Is.EquivalentTo([_episode1.Id]));
            Assert.That(result.ActionableNodeIds, Is.EquivalentTo([_episode1.Id]));
        }

        _contentHierarchyItems.Verify(
            value => value.GetLineageAsync(_season1.Id, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ResolveScopeAsync_ReturnsMatchingEpisodes_WhenMultiEpisodeReleaseDoesNotCoverSeason()
    {
        // Arrange
        var release = new Release { Protocol = "torrent", Title = "Show.S01E01E02" };
        _matchingIds.UnionWith([_season1.Id, _episode1.Id, _episode2.Id]);

        // Act
        var result = await _subject.ResolveScopeAsync(release, [], _season1.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ScopeNodeIds, Is.EquivalentTo([_episode1.Id, _episode2.Id]));
            Assert.That(result.ActionableNodeIds, Is.EquivalentTo([_episode1.Id, _episode2.Id]));
        }
    }

    [Test]
    public async Task ResolveScopeAsync_ReturnsSeasonAndEpisodes_WhenSeasonPackCoversEveryEpisode()
    {
        // Arrange
        var release = new Release { Protocol = "torrent", Title = "Show.S01.Complete" };
        IReadOnlyList<ContentFact> releaseFacts =
        [new ContentFact { Value = "Show", Facets = new Dictionary<string, string> { ["season"] = "1" }, },];
        _matchingIds.UnionWith([_season1.Id, _episode1.Id, _episode2.Id, _episode3.Id]);

        // Act
        var result = await _subject.ResolveScopeAsync(release, releaseFacts, _season1.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ScopeNodeIds, Is.EquivalentTo([_season1.Id, _episode1.Id, _episode2.Id, _episode3.Id]));
            Assert.That(result.ActionableNodeIds, Is.EquivalentTo([_episode1.Id, _episode2.Id, _episode3.Id]));
        }

        _contentHierarchyItems.Verify(
            value => value.GetLineageAsync(_episode1.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ResolveScopeAsync_ReturnsCoveredSeasons_WhenSeasonRangeDoesNotCoverShow()
    {
        // Arrange
        var release = new Release { Protocol = "torrent", Title = "Show.S01-S02.Complete" };
        _matchingIds.UnionWith(
        [
            _show.Id,
            _season1.Id,
            _season2.Id,
            _episode1.Id,
            _episode2.Id,
            _episode3.Id,
            _episode4.Id,
            _episode5.Id,
        ]);

        // Act
        var result = await _subject.ResolveScopeAsync(release, [], _show.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.ScopeNodeIds,
                Is.EquivalentTo(
                [
                    _season1.Id,
                    _season2.Id,
                    _episode1.Id,
                    _episode2.Id,
                    _episode3.Id,
                    _episode4.Id,
                    _episode5.Id,
                ]));
            Assert.That(
                result.ActionableNodeIds,
                Is.EquivalentTo([_episode1.Id, _episode2.Id, _episode3.Id, _episode4.Id, _episode5.Id]));
        }
    }

    [Test]
    public async Task ResolveScopeAsync_ReturnsEntireSubtree_WhenSeriesPackCoversEveryEpisode()
    {
        // Arrange
        var release = new Release { Protocol = "torrent", Title = "Show.Complete.Series" };
        _matchingIds.UnionWith(
        [
            _show.Id,
            _season1.Id,
            _season2.Id,
            _season3.Id,
            _episode1.Id,
            _episode2.Id,
            _episode3.Id,
            _episode4.Id,
            _episode5.Id,
            _episode6.Id,
        ]);

        // Act
        var result = await _subject.ResolveScopeAsync(release, [], _show.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.ScopeNodeIds,
                Is.EquivalentTo(
                [
                    _show.Id,
                    _season1.Id,
                    _season2.Id,
                    _season3.Id,
                    _episode1.Id,
                    _episode2.Id,
                    _episode3.Id,
                    _episode4.Id,
                    _episode5.Id,
                    _episode6.Id,
                ]));
            Assert.That(
                result.ActionableNodeIds,
                Is.EquivalentTo(
                [_episode1.Id, _episode2.Id, _episode3.Id, _episode4.Id, _episode5.Id, _episode6.Id]));
        }
    }
}
