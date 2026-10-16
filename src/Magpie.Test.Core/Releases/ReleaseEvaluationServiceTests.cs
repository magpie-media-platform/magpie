using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Releases;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
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
    private Mock<IReleaseMatchContentTypeDefinitionProvider> _releaseMatchProvider = null!;
    private Mock<IReleaseRepository> _releases = null!;
    private Mock<IDownloadRepository> _downloads = null!;
    private Mock<IQualityTierListRepository> _qualityTierLists = null!;
    private ReleaseEvaluationService _subject = null!;
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

        _contentHierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
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

        var materializedMetadata = new Mock<IMaterializedMetadataRepository> { CallBase = true };
        materializedMetadata
            .Setup(value => value.QueryAsync(
                It.IsAny<MetadataCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MaterializedMetadata>());

        _releaseMatchProvider = new Mock<IReleaseMatchContentTypeDefinitionProvider>();
        _releaseMatchProvider
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
        contentTypeRegistry.Setup(value => value.FindProviderFor("tv")).Returns(_releaseMatchProvider.Object);
        contentTypeRegistry.Setup(value => value.GetDefinitionFor("tv")).Returns(new ContentTypeDefinition
        {
            Slug = "tv",
            DisplayName = "TV",
            LevelDefinitions =
            [
                new()
                {
                    Slug = "show", DisplayName = "Show", AcceptedFileTypes = [".mkv"],
                    AcceptedSidecarTypes = [".srt"], CustomMetadataFields = []
                }
            ]
        });
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
        _releases = new Mock<IReleaseRepository> { CallBase = true };
        _downloads = new Mock<IDownloadRepository> { CallBase = true };
        _qualityTierLists = new Mock<IQualityTierListRepository> { CallBase = true };
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(value => value.MatchesAnyFileTypeAsync(
                It.Is<string>(path => path.EndsWith(".mkv", StringComparison.Ordinal)),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _subject = new ReleaseEvaluationService(
            _contentHierarchyItems.Object,
            materializedMetadata.Object,
            _releases.Object,
            _downloads.Object,
            _qualityTierLists.Object,
            contentTypeRegistry.Object,
            [_contentFactProvider.Object],
            TimeProvider.System,
            fileSystem.Object,
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
        using (Assert.EnterMultipleScope())
        {
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
    }

    [Test]
    public async Task CreateEvaluationContextAsync_UsesReconciledQuality_WhenHistoricalDownloadsExist()
    {
        // Arrange
        _show.ContentItem = new ContentItem
        {
            ContentHierarchyItemId = _show.Id,
            MediaReconciledAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            CurrentFileQualityRank = 4
        };

        // Act
        var result = await _subject.CreateEvaluationContextAsync(_show.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.CurrentFileQualityRank, Is.EqualTo(4));
            _downloads.Verify(repository => repository.QueryAsync(It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(720, true)]
    [TestCase(1080, false)]
    public async Task ScoreFileForContentAsync_UsesCurrentMediaProperties_WhenAssessingReplacement(
        int height,
        bool expected)
    {
        // Arrange
        var listId = Guid.NewGuid();
        _show.EffectiveQualityTierListId = listId;
        _qualityTierLists.Setup(repository => repository.GetByIdAsync(listId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualityTierList
            {
                Id = listId,
                Name = "Current media",
                QualityTierRanks =
                [
                    new()
                    {
                        Ordinal = 4, QualityTier = new QualityTier
                        {
                            Name = "720", Criteria = new QualityTierCriteria
                            {
                                And = [new DictionaryCriteria<PropertyCriterion<string>>
                                {
                                    Items = new Dictionary<string, PropertyCriterion<string>>
                                    {
                                        ["Height"] = new() { Operator = CriteriaValueOperator.Equals, Value = "720" },
                                        ["Size"] = new() { Operator = CriteriaValueOperator.Equals, Value = "400" },
                                        ["Language.en"] = new()
                                        {
                                            Operator = CriteriaValueOperator.Equals, Value = "true"
                                        }
                                    }
                                }]
                            }
                        }
                    }
                ]
            });
        var file = new MediaFile
        {
            FilePath = "/media/current.mkv",
            FileKind = "Video",
            FileSize = 400,
            MediaInfo = new() { Tracks = [new() { Name = "Video", Type = "Video", Height = height, Language = "en" }] }
        };

        // Act
        var result = await _subject.ScoreFileForContentAsync(file, _show.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.IsAcceptable, Is.EqualTo(expected));
            _downloads.Verify(repository => repository.QueryAsync(It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(new[] { 1080 }, true, QualityStatus.UpgradeWanted, 1)]
    [TestCase(new[] { 2160 }, true, QualityStatus.Met, 0)]
    [TestCase(new[] { 1080 }, false, QualityStatus.Met, 1)]
    [TestCase(new[] { 1080, 2160 }, true, QualityStatus.Met, 0)]
    [TestCase(new[] { 480 }, true, null, null)]
    public async Task ScoreMediaQualityAsync_ScoresTheBestStoredFileAgainstTheCutoff_WhenFilesArePrimary(
        int[] heights,
        bool upgradeAllowed,
        QualityStatus? expectedQuality,
        int? expectedRank)
    {
        // Arrange
        var listId = Guid.NewGuid();
        static QualityTierListRank Rank(int ordinal, int height, bool isCutoff) => new()
        {
            Ordinal = ordinal,
            IsCutoff = isCutoff,
            QualityTier = new QualityTier
            {
                Name = $"{height}p",
                Criteria = new QualityTierCriteria
                {
                    And = [new DictionaryCriteria<PropertyCriterion<string>>
                    {
                        Items = new Dictionary<string, PropertyCriterion<string>>
                        {
                            ["Height"] = new() { Operator = CriteriaValueOperator.Equals, Value = $"{height}" }
                        }
                    }]
                }
            }
        };
        _qualityTierLists.Setup(repository => repository.GetByIdAsync(listId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualityTierList
            {
                Id = listId,
                Name = "Stored media",
                UpgradeAllowed = upgradeAllowed,
                QualityTierRanks = [Rank(0, 2160, isCutoff: true), Rank(1, 1080, isCutoff: false)]
            });
        var files = heights.Select(height => new MediaFile
        {
            FilePath = $"/media/stored-{height}.mkv",
            FileKind = "Video",
            FileSize = 400,
            MediaInfo = new() { Tracks = [new() { Name = "Video", Type = "Video", Height = height }] }
        });

        // Act
        var (quality, rank) = await _subject.ScoreMediaQualityAsync(files, listId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(quality, Is.EqualTo(expectedQuality));
            Assert.That(rank, Is.EqualTo(expectedRank));
        }
    }

    [TestCase("/media/Show S01E01.mkv", true, true, true, "Show S01E01 1080p WEB-DL-GRP", 1, true, false, 1)]
    [TestCase("/media/Show S01E01.mkv", false, true, true, "Show S01E01 1080p WEB-DL-GRP", null, false, false, 0)]
    [TestCase("/media/Show S01E01 2160p.mkv", true, true, true, "Show S01E01 1080p WEB-DL-GRP", 0, true, false, 0)]
    [TestCase("/media/Show S01E01 CAM.mkv", true, true, true, "Show S01E01 1080p WEB-DL-GRP", null, false, true, 0)]
    [TestCase("/media/Show S01E01.mkv", true, true, true, "Show S01E01-GRP", 1, true, false, 1)]
    [TestCase("/media/Show S01E01.mkv", true, false, false, "Show S01E01 1080p WEB-DL-GRP", 1, true, false, 1)]
    [TestCase("/media/Show S01E01.mkv", true, true, false, "Show S01E01 1080p WEB-DL-GRP", 1, true, false, 1)]
    [TestCase("/media/Show S01E01.mkv", true, false, false, "Show S01E01-GRP", null, false, false, 1)]
    public async Task ScoreFileForContentAsync_UsesLinkedImportQuality_WhenFileHasNoQuality(
        string filePath,
        bool linked,
        bool hasReleaseId,
        bool releaseStored,
        string downloadTitle,
        int? expectedRank,
        bool expectedCutoffMet,
        bool expectedExcluded,
        int expectedDownloadReads)
    {
        // Arrange
        var listId = Guid.NewGuid();
        var downloadId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        _show.EffectiveQualityTierListId = listId;
        _qualityTierLists.Setup(repository => repository.GetByIdAsync(listId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualityTierList
            {
                Id = listId,
                Name = "Linked import",
                Rules =
                [
                    new()
                    {
                        Kind = RuleKind.Exclude, Criteria = new QualityTierCriteria
                        {
                            And = [new DictionaryCriteria<PropertyCriterion<string>>
                            {
                                Items = new Dictionary<string, PropertyCriterion<string>>
                                {
                                    ["Title"] = new() { Operator = CriteriaValueOperator.ContainsWord, Value = "CAM" }
                                }
                            }]
                        }
                    }
                ],
                QualityTierRanks =
                [
                    new()
                    {
                        Ordinal = 0, QualityTier = new QualityTier
                        {
                            Name = "2160p", Criteria = new QualityTierCriteria
                            {
                                And = [new DictionaryCriteria<PropertyCriterion<string>>
                                {
                                    Items = new Dictionary<string, PropertyCriterion<string>>
                                    {
                                        ["Title"] = new()
                                        {
                                            Operator = CriteriaValueOperator.ContainsString, Value = "2160p"
                                        }
                                    }
                                }]
                            }
                        }
                    },
                    new()
                    {
                        Ordinal = 1, IsCutoff = true, QualityTier = new QualityTier
                        {
                            Name = "1080p", Criteria = new QualityTierCriteria
                            {
                                And = [new DictionaryCriteria<PropertyCriterion<string>>
                                {
                                    Items = new Dictionary<string, PropertyCriterion<string>>
                                    {
                                        ["Title"] = new()
                                        {
                                            Operator = CriteriaValueOperator.ContainsString, Value = "1080p"
                                        }
                                    }
                                }]
                            }
                        }
                    }
                ]
            });
        _downloads.Setup(repository => repository.GetByIdAsync(downloadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Download
            {
                Id = downloadId,
                ReleaseId = hasReleaseId ? releaseId : null,
                ExternalId = "linked",
                Title = downloadTitle,
                Status = DownloadStatus.Imported
            });
        _releases.Setup(repository => repository.GetByIdAsync(releaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(releaseStored
                ? new Release { Id = releaseId, Title = "Show S01E01 1080p WEB-DL-GRP", Protocol = "torrent" }
                : null);
        var file = new MediaFile
        {
            FilePath = filePath,
            FileKind = "Video",
            FileSize = 400,
            DownloadId = linked ? downloadId : null
        };

        // Act
        var result = await _subject.ScoreFileForContentAsync(file, _show.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.MatchedRankOrdinal, Is.EqualTo(expectedRank));
            Assert.That(result.IsCutoffMet, Is.EqualTo(expectedCutoffMet));
            Assert.That(result.IsExcluded, Is.EqualTo(expectedExcluded));
        }

        _downloads.Verify(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Exactly(expectedDownloadReads));
    }

    [TestCase(true, "Show S01E01 1080p WEB-DL-GRP", "Show S01E01-GRP", 1, true)]
    [TestCase(true, "Show S01E01-GRP", "Show S01E01 1080p WEB-DL-GRP", null, false)]
    [TestCase(false, null, "Show S01E01 2160p WEB-DL-GRP", 0, true)]
    [TestCase(false, null, "Show S01E01-GRP", null, null)]
    [TestCase(false, null, "Show S01E01 CAM-GRP", null, null)]
    public async Task ScoreDownloadForContentAsync_ScoresTheStoredReleaseOrTheTitleAlone_WhenTheReleaseIsStoredOrNot(
        bool releaseStored,
        string? releaseTitle,
        string downloadTitle,
        int? expectedRank,
        bool? expectedAcceptable)
    {
        // Arrange
        var listId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        _show.EffectiveQualityTierListId = listId;
        _qualityTierLists.Setup(repository => repository.GetByIdAsync(listId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualityTierList
            {
                Id = listId,
                Name = "Download",
                Rules =
                [
                    new()
                    {
                        Kind = RuleKind.Exclude, Criteria = new QualityTierCriteria
                        {
                            And = [new DictionaryCriteria<PropertyCriterion<string>>
                            {
                                Items = new Dictionary<string, PropertyCriterion<string>>
                                {
                                    ["Title"] = new() { Operator = CriteriaValueOperator.ContainsWord, Value = "CAM" }
                                }
                            }]
                        }
                    }
                ],
                QualityTierRanks =
                [
                    new()
                    {
                        Ordinal = 0, QualityTier = new QualityTier
                        {
                            Name = "2160p", Criteria = new QualityTierCriteria
                            {
                                And = [new DictionaryCriteria<PropertyCriterion<string>>
                                {
                                    Items = new Dictionary<string, PropertyCriterion<string>>
                                    {
                                        ["Title"] = new()
                                        {
                                            Operator = CriteriaValueOperator.ContainsString, Value = "2160p"
                                        }
                                    }
                                }]
                            }
                        }
                    },
                    new()
                    {
                        Ordinal = 1, IsCutoff = true, QualityTier = new QualityTier
                        {
                            Name = "1080p", Criteria = new QualityTierCriteria
                            {
                                And = [new DictionaryCriteria<PropertyCriterion<string>>
                                {
                                    Items = new Dictionary<string, PropertyCriterion<string>>
                                    {
                                        ["Title"] = new()
                                        {
                                            Operator = CriteriaValueOperator.ContainsString, Value = "1080p"
                                        }
                                    }
                                }]
                            }
                        }
                    }
                ]
            });
        _releases.Setup(repository => repository.GetByIdAsync(releaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(releaseStored
                ? new Release { Id = releaseId, Title = releaseTitle!, Protocol = "torrent" }
                : null);
        var download = new Download
        {
            ReleaseId = releaseId,
            ExternalId = "imported",
            Title = downloadTitle,
            Status = DownloadStatus.Imported
        };

        // Act
        var result = await _subject.ScoreDownloadForContentAsync(download, _show.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result?.MatchedRankOrdinal, Is.EqualTo(expectedRank));
            Assert.That(result?.IsAcceptable, Is.EqualTo(expectedAcceptable));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CreateEvaluationContextAsync_UsesLinkedImportGroup_WhenReconciledFileHasNoQuality(
        bool coveringFile)
    {
        // Arrange
        var listId = Guid.NewGuid();
        var downloadId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        var file = new MediaFile { FilePath = "Show S01E01.mkv", FileKind = "Video", DownloadId = downloadId };
        _show.EffectiveQualityTierListId = listId;
        _show.ContentItem = new ContentItem
        {
            ContentHierarchyItemId = _show.Id,
            MediaReconciledAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            PrimaryMediaFileCount = 1,
            CurrentFileQualityRank = 1
        };
        (coveringFile ? _show.ContentItem.CoveringMediaFiles : _show.ContentItem.MediaFiles).Add(file);
        _qualityTierLists.Setup(repository => repository.GetByIdAsync(listId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualityTierList
            {
                Id = listId,
                Name = "Linked import",
                QualityTierRanks =
                [
                    new()
                    {
                        Ordinal = 1, IsCutoff = true, QualityTier = new QualityTier
                        {
                            Name = "1080p", Criteria = new QualityTierCriteria
                            {
                                And = [new DictionaryCriteria<PropertyCriterion<string>>
                                {
                                    Items = new Dictionary<string, PropertyCriterion<string>>
                                    {
                                        ["Title"] = new()
                                        {
                                            Operator = CriteriaValueOperator.ContainsString, Value = "1080p"
                                        }
                                    }
                                }]
                            }
                        }
                    }
                ]
            });
        _downloads.Setup(repository => repository.GetByIdAsync(downloadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Download
            {
                Id = downloadId,
                ReleaseId = releaseId,
                ExternalId = "linked",
                Title = "Show S01E01 1080p PROPER-GRP",
                Status = DownloadStatus.Imported
            });
        _releases.Setup(repository => repository.GetByIdAsync(releaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Release { Id = releaseId, Title = "Show S01E01 1080p PROPER-GRP", Protocol = "torrent" });

        // Act
        var result = await _subject.CreateEvaluationContextAsync(_show.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.CurrentFileQualityRank, Is.EqualTo(1));
            Assert.That(result.CurrentFileGroup, Is.EqualTo("GRP"));
            Assert.That(result.CurrentFileRevision?.Version, Is.EqualTo(2));
        }
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

    [TestCase("Show S01E01 1080p PROPER-GROUP", "Show S01E01 1080p-GROUP", true)]
    [TestCase("Show S01E01 1080p PROPER-OTHER", "Show S01E01 1080p-GROUP", false)]
    [TestCase("Show S01E01 1080p PROPER", "Show S01E01 1080p", false)]
    [TestCase("Show S01E01 1080p-GROUP", "Show S01E01 1080p-GROUP", false)]
    [TestCase("Show S01E01 1080p PROPER-GROUP", "Show S01E01 1080p-GROUP", true, true)]
    [TestCase("Show S01E01 1080p PROPER-OTHER", "Show S01E01 1080p-GROUP", false, true)]
    [TestCase("Show S01E01 1080p PROPER", "Show S01E01 1080p", false, true)]
    [TestCase("Show S01E01 1080p-GROUP", "Show S01E01 1080p-GROUP", false, true)]
    public async Task AssessForContentAsync_AcceptsOnlyTheSameGroup_WhenTierAndRevisionTie(
        string title,
        string currentFileTitle,
        bool expected,
        bool reconciled = false)
    {
        // Arrange
        var tierListId = Guid.NewGuid();
        var importedReleaseId = Guid.NewGuid();
        var tier = new QualityTier
        {
            Id = Guid.NewGuid(),
            Name = "1080p",
            Criteria = new QualityTierCriteria
            {
                And =
                [
                    new DictionaryCriteria<PropertyCriterion<string>>
                    {
                        Items = new Dictionary<string, PropertyCriterion<string>>
                        {
                            ["Title"] = new()
                            {
                                Operator = CriteriaValueOperator.ContainsString,
                                Value = "1080p"
                            }
                        }
                    }
                ]
            },
            CreatedAt = DateTime.UtcNow
        };
        _show.EffectiveQualityTierListId = tierListId;
        if (reconciled)
        {
            _show.ContentItem = new ContentItem
            {
                ContentHierarchyItemId = _show.Id,
                MediaReconciledAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
                CurrentFileQualityRank = 2,
                MediaFiles =
                {
                    new MediaFile { FilePath = currentFileTitle + ".mkv", FileKind = "Video" },
                    new MediaFile { FilePath = "Show S01E01 1080p PROPER-GROUP.srt", FileKind = "Subtitle" }
                }
            };
        }

        _qualityTierLists
            .Setup(repository => repository.GetByIdAsync(tierListId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualityTierList
            {
                Id = tierListId,
                Name = "List",
                CreatedAt = DateTime.UtcNow,
                QualityTierRanks =
                [
                    new QualityTierListRank
                    {
                        QualityTierListId = tierListId,
                        QualityTierId = tier.Id,
                        QualityTier = tier,
                        Ordinal = 2,
                        CreatedAt = DateTime.UtcNow
                    }
                ]
            });
        _downloads
            .Setup(repository => repository.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items =
                [
                    new Download
                    {
                        Id = Guid.NewGuid(),
                        ReleaseId = importedReleaseId,
                        ExternalId = "download-1",
                        Title = currentFileTitle,
                        Status = DownloadStatus.Imported,
                        CreatedAt = DateTime.UtcNow
                    }
                ],
                TotalItems = 1
            });
        _releases
            .Setup(repository => repository.GetByIdAsync(importedReleaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Release
            {
                Id = importedReleaseId,
                Title = reconciled ? "Show S01E01 1080p PROPER-STALE" : currentFileTitle,
                Protocol = "torrent"
            });

        // Act
        var assessment = await _subject.AssessForContentAsync(
                new Release { Title = title, Protocol = "torrent" },
                [],
                _show.Id)
            .ConfigureAwait(false);

        // Assert
        Assert.That(assessment.IsAcceptable, Is.EqualTo(expected));
    }

    [TestCase(true, true, 1)]
    [TestCase(false, true, null)]
    [TestCase(true, false, 1)]
    public async Task CreateEvaluationContextAsync_UsesTheLinkedImportQuality_WhenReconciledFileHasNoRank(
        bool linked,
        bool releaseStored,
        int? expectedRank)
    {
        // Arrange
        var tierListId = Guid.NewGuid();
        var linkedDownloadId = Guid.NewGuid();
        var linkedReleaseId = Guid.NewGuid();
        _show.EffectiveQualityTierListId = tierListId;
        _show.ContentItem = new ContentItem
        {
            ContentHierarchyItemId = _show.Id,
            MediaReconciledAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            PrimaryMediaFileCount = 1,
            CurrentFileQualityRank = null,
            MediaFiles =
            {
                new MediaFile
                {
                    FilePath = "Show S01E01.mkv",
                    FileKind = "Video",
                    DownloadId = linked ? linkedDownloadId : null
                }
            }
        };
        _qualityTierLists
            .Setup(repository => repository.GetByIdAsync(tierListId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualityTierList
            {
                Id = tierListId,
                Name = "List",
                CreatedAt = DateTime.UtcNow,
                QualityTierRanks =
                [
                    new QualityTierListRank
                    {
                        QualityTierListId = tierListId,
                        Ordinal = 0,
                        QualityTier = new QualityTier
                        {
                            Name = "2160p",
                            Criteria = new QualityTierCriteria
                            {
                                And =
                                [
                                    new DictionaryCriteria<PropertyCriterion<string>>
                                    {
                                        Items = new Dictionary<string, PropertyCriterion<string>>
                                        {
                                            ["Title"] = new()
                                            {
                                                Operator = CriteriaValueOperator.ContainsString, Value = "2160p"
                                            }
                                        }
                                    }
                                ]
                            }
                        }
                    },
                    new QualityTierListRank
                    {
                        QualityTierListId = tierListId,
                        Ordinal = 1,
                        QualityTier = new QualityTier
                        {
                            Name = "1080p",
                            Criteria = new QualityTierCriteria
                            {
                                And =
                                [
                                    new DictionaryCriteria<PropertyCriterion<string>>
                                    {
                                        Items = new Dictionary<string, PropertyCriterion<string>>
                                        {
                                            ["Title"] = new()
                                            {
                                                Operator = CriteriaValueOperator.ContainsString, Value = "1080p"
                                            }
                                        }
                                    }
                                ]
                            }
                        }
                    },
                    new QualityTierListRank
                    {
                        QualityTierListId = tierListId,
                        Ordinal = 2,
                        QualityTier = new QualityTier
                        {
                            Name = "720p",
                            Criteria = new QualityTierCriteria
                            {
                                And =
                                [
                                    new DictionaryCriteria<PropertyCriterion<string>>
                                    {
                                        Items = new Dictionary<string, PropertyCriterion<string>>
                                        {
                                            ["Title"] = new()
                                            {
                                                Operator = CriteriaValueOperator.ContainsString, Value = "720p"
                                            }
                                        }
                                    }
                                ]
                            }
                        }
                    }
                ]
            });
        _downloads
            .Setup(repository => repository.GetByIdAsync(linkedDownloadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Download
            {
                Id = linkedDownloadId,
                ReleaseId = linkedReleaseId,
                ExternalId = "linked",
                Title = "Show S01E01 1080p-X",
                Status = DownloadStatus.Imported,
                DateCompleted = new DateTime(2026, 9, 21, 1, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
            });
        _downloads
            .Setup(repository => repository.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items =
                [
                    new Download
                    {
                        Id = Guid.NewGuid(),
                        ReleaseId = Guid.NewGuid(),
                        ExternalId = "later-completed",
                        Title = "Show S01E01 720p-A",
                        Status = DownloadStatus.Imported,
                        DateCompleted = new DateTime(2026, 9, 21, 2, 0, 0, DateTimeKind.Utc),
                        CreatedAt = new DateTime(2026, 9, 21, 2, 0, 0, DateTimeKind.Utc)
                    }
                ],
                TotalItems = 1
            });
        _releases
            .Setup(repository => repository.GetByIdAsync(linkedReleaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(releaseStored
                ? new Release { Id = linkedReleaseId, Title = "Show S01E01 1080p-X", Protocol = "torrent" }
                : null);
        _releases
            .Setup(repository => repository.GetByIdAsync(
                It.Is<Guid>(id => id != linkedReleaseId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Release { Title = "Show S01E01 720p-A", Protocol = "torrent" });

        // Act
        var result = await _subject.CreateEvaluationContextAsync(_show.Id).ConfigureAwait(false);

        // Assert
        Assert.That(result.CurrentFileQualityRank, Is.EqualTo(expectedRank));
    }

    [Test]
    public async Task CreateEvaluationContextAsync_UsesOnlyImportedDownloads_WhenMediaIsNotReconciled()
    {
        // Arrange
        var tierListId = Guid.NewGuid();
        var importedReleaseId = Guid.NewGuid();
        var cancelledReleaseId = Guid.NewGuid();
        var activeReleaseId = Guid.NewGuid();
        _show.EffectiveQualityTierListId = tierListId;
        _qualityTierLists
            .Setup(repository => repository.GetByIdAsync(tierListId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualityTierList
            {
                Id = tierListId,
                Name = "List",
                CreatedAt = DateTime.UtcNow,
                QualityTierRanks =
                [
                    new QualityTierListRank
                    {
                        QualityTierListId = tierListId,
                        Ordinal = 1,
                        QualityTier = new QualityTier
                        {
                            Name = "1080p",
                            Criteria = new QualityTierCriteria
                            {
                                And =
                                [
                                    new DictionaryCriteria<PropertyCriterion<string>>
                                    {
                                        Items = new Dictionary<string, PropertyCriterion<string>>
                                        {
                                            ["Title"] = new()
                                            {
                                                Operator = CriteriaValueOperator.ContainsString, Value = "1080p"
                                            }
                                        }
                                    }
                                ]
                            }
                        }
                    },
                    new QualityTierListRank
                    {
                        QualityTierListId = tierListId,
                        Ordinal = 2,
                        QualityTier = new QualityTier
                        {
                            Name = "720p",
                            Criteria = new QualityTierCriteria
                            {
                                And =
                                [
                                    new DictionaryCriteria<PropertyCriterion<string>>
                                    {
                                        Items = new Dictionary<string, PropertyCriterion<string>>
                                        {
                                            ["Title"] = new()
                                            {
                                                Operator = CriteriaValueOperator.ContainsString, Value = "720p"
                                            }
                                        }
                                    }
                                ]
                            }
                        }
                    }
                ]
            });
        _downloads
            .Setup(repository => repository.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items =
                [
                    new Download
                    {
                        Id = Guid.NewGuid(),
                        ReleaseId = importedReleaseId,
                        ExternalId = "imported",
                        Title = "Show S01E01 720p-A",
                        Status = DownloadStatus.Imported,
                        CreatedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new Download
                    {
                        Id = Guid.NewGuid(),
                        ReleaseId = cancelledReleaseId,
                        ExternalId = "cancelled",
                        Title = "Show S01E01 1080p-B",
                        Status = DownloadStatus.Cancelled,
                        CreatedAt = new DateTime(2026, 9, 21, 1, 0, 0, DateTimeKind.Utc)
                    },
                    new Download
                    {
                        Id = Guid.NewGuid(),
                        ReleaseId = activeReleaseId,
                        ExternalId = "active",
                        Title = "Show S01E01 1080p-C",
                        Status = DownloadStatus.Downloading,
                        CreatedAt = new DateTime(2026, 9, 21, 2, 0, 0, DateTimeKind.Utc)
                    }
                ],
                TotalItems = 3
            });
        _releases
            .Setup(repository => repository.GetByIdAsync(importedReleaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Release { Id = importedReleaseId, Title = "Show S01E01 720p-A", Protocol = "torrent" });
        _releases
            .Setup(repository => repository.GetByIdAsync(
                It.Is<Guid>(id => id != importedReleaseId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Release { Title = "Show S01E01 1080p-B", Protocol = "torrent" });

        // Act
        var result = await _subject.CreateEvaluationContextAsync(_show.Id).ConfigureAwait(false);

        // Assert
        Assert.That(result.CurrentFileQualityRank, Is.EqualTo(2));
    }

    [Test]
    public async Task CreateEvaluationContextAsync_UsesTheNewestImportTitle_WhenItsReleaseIsNotStored()
    {
        // Arrange
        var tierListId = Guid.NewGuid();
        var importedReleaseId = Guid.NewGuid();
        _show.EffectiveQualityTierListId = tierListId;
        _qualityTierLists
            .Setup(repository => repository.GetByIdAsync(tierListId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualityTierList
            {
                Id = tierListId,
                Name = "List",
                CreatedAt = DateTime.UtcNow,
                QualityTierRanks =
                [
                    new QualityTierListRank
                    {
                        QualityTierListId = tierListId,
                        Ordinal = 1,
                        QualityTier = new QualityTier
                        {
                            Name = "1080p",
                            Criteria = new QualityTierCriteria
                            {
                                And =
                                [
                                    new DictionaryCriteria<PropertyCriterion<string>>
                                    {
                                        Items = new Dictionary<string, PropertyCriterion<string>>
                                        {
                                            ["Title"] = new()
                                            {
                                                Operator = CriteriaValueOperator.ContainsString, Value = "1080p"
                                            }
                                        }
                                    }
                                ]
                            }
                        }
                    },
                    new QualityTierListRank
                    {
                        QualityTierListId = tierListId,
                        Ordinal = 2,
                        QualityTier = new QualityTier
                        {
                            Name = "720p",
                            Criteria = new QualityTierCriteria
                            {
                                And =
                                [
                                    new DictionaryCriteria<PropertyCriterion<string>>
                                    {
                                        Items = new Dictionary<string, PropertyCriterion<string>>
                                        {
                                            ["Title"] = new()
                                            {
                                                Operator = CriteriaValueOperator.ContainsString, Value = "720p"
                                            }
                                        }
                                    }
                                ]
                            }
                        }
                    }
                ]
            });
        _downloads
            .Setup(repository => repository.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items =
                [
                    new Download
                    {
                        Id = Guid.NewGuid(),
                        ReleaseId = null,
                        ExternalId = "newest",
                        Title = "Show S01E01 1080p PROPER-NEW",
                        Status = DownloadStatus.Imported,
                        CreatedAt = new DateTime(2026, 9, 21, 2, 0, 0, DateTimeKind.Utc)
                    },
                    new Download
                    {
                        Id = Guid.NewGuid(),
                        ReleaseId = importedReleaseId,
                        ExternalId = "older",
                        Title = "Show S01E01 720p-OLD",
                        Status = DownloadStatus.Imported,
                        CreatedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
                    }
                ],
                TotalItems = 2
            });
        _releases
            .Setup(repository => repository.GetByIdAsync(importedReleaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Release { Id = importedReleaseId, Title = "Show S01E01 720p-OLD", Protocol = "torrent" });

        // Act
        var result = await _subject.CreateEvaluationContextAsync(_show.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.CurrentFileQualityRank, Is.EqualTo(1));
            Assert.That(result.CurrentFileGroup, Is.EqualTo("NEW"));
            Assert.That(result.CurrentFileRevision?.Version, Is.EqualTo(2));
        }
    }
}
