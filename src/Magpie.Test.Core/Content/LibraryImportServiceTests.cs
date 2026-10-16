using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Content;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Import;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Models.Releases;
using Magpie.Sdk.Content;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class LibraryImportServiceTests
{
    private List<LibraryImportCandidate> _candidates = null!;
    private List<LibraryImportCandidateGroupSummary> _groupSummaries = null!;
    private Dictionary<string, ContentTypeDefinition> _definitions = null!;
    private Mock<ILibraryImportCandidateRepository> _candidateRepository = null!;
    private Mock<IContentService> _contentService = null!;
    private ILibraryImportService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _candidates = [];
        _groupSummaries = [];
        _definitions = [];

        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<LibraryImportCandidateQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate>());
        candidateRepository
            .Setup(r => r.QueryIdsAsync(
                It.IsAny<LibraryImportCandidateQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Guid>());
        candidateRepository
            .Setup(r => r.QueryGroupsAsync(
                It.IsAny<LibraryImportCandidateQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new PagedList<LibraryImportCandidateGroupSummary>
            {
                Items = _groupSummaries,
                TotalItems = _groupSummaries.Count
            });
        candidateRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _candidates.Find(candidate => candidate.Id == id));
        _candidateRepository = candidateRepository;

        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry
            .Setup(r => r.GetDefinitionFor(It.IsAny<string>()))
            .Returns((string contentType) => _definitions.GetValueOrDefault(contentType));
        contentTypeRegistry
            .Setup(r => r.GetRootLevels(It.IsAny<string>()))
            .Returns((string contentType) => _definitions.TryGetValue(contentType, out var definition)
                ? [.. definition.LevelDefinitions.Where(level => level.ParentSlug is null)]
                : []);
        contentTypeRegistry
            .Setup(r => r.GetChildLevels(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string contentType, string levelSlug) => _definitions.TryGetValue(contentType, out var definition)
                ? [.. definition.LevelDefinitions.Where(level => level.ParentSlug == levelSlug)]
                : []);

        _contentService = new Mock<IContentService>();
        _subject = new LibraryImportService(
            candidateRepository.Object,
            _contentService.Object,
            Mock.Of<IContentFileService>(),
            contentTypeRegistry.Object,
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);
    }

    [TestCase(
        true,
        "The season level below show 'Bluey' does not exist yet because its metadata refresh is pending.")]
    [TestCase(false, "The target child level does not exist yet.")]
    public async Task PreviewRenamesAsync_ReportsChildLevelState_WhenTargetHasNoChildren(
        bool pendingMetadataRefresh,
        string expected)
    {
        // Arrange
        _definitions["tv"] = new ContentTypeDefinition
        {
            Slug = "tv",
            DisplayName = "TV",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "show", ParentSlug = null, DisplayName = "Show", CustomMetadataFields = []
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "season", ParentSlug = "show", DisplayName = "Season", CustomMetadataFields = []
                }
            ]
        };
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/S01E01.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Pending,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Bluey",
                    LevelSlug = "show"
                }
            ]
        };
        _candidates.Add(candidate);
        _contentService
            .Setup(service => service.PreviewImportHierarchyAsync(
                It.IsAny<PreviewByExternalIdRequest>(),
                candidate.RootFolderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ContentHierarchyItem
                {
                    Id = Guid.NewGuid(),
                    ContentType = "tv",
                    Level = "show",
                    Name = "Bluey",
                    Slug = "bluey",
                    PendingMetadataRefresh = pendingMetadataRefresh
                }
            ]);

        // Act
        var preview = await _subject
            .PreviewRenamesAsync(new LibraryImportRenameSelection { Ids = [candidate.Id] })
            .ConfigureAwait(false);

        // Assert
        Assert.That(preview.Files.Single().Error, Is.EqualTo(expected));
    }

    [Test]
    public async Task PreviewRenamesAsync_PreviewsTheMaskWithTheCoveredEpisodes_WhenTheFileNamesSeveralEpisodes()
    {
        // Arrange
        var show = new ContentHierarchyItem
        {
            Id = new Guid("00000000-0000-0000-0000-00000000a001"),
            ContentType = "tv",
            Level = "show",
            Name = "Bluey",
            Slug = "bluey"
        };
        var season = new ContentHierarchyItem
        {
            Id = new Guid("00000000-0000-0000-0000-00000000b001"),
            ParentId = show.Id,
            ContentType = "tv",
            Level = "season",
            Name = "Season 1",
            Slug = "season-1",
            Ordinal = 1
        };
        ContentHierarchyItem[] episodes =
        [
            new()
            {
                Id = new Guid("00000000-0000-0000-0000-000000000001"),
                ParentId = season.Id,
                ContentType = "tv",
                Level = "episode",
                Name = "E1",
                Slug = "s01e01",
                Ordinal = 1
            },
            new()
            {
                Id = new Guid("00000000-0000-0000-0000-000000000002"),
                ParentId = season.Id,
                ContentType = "tv",
                Level = "episode",
                Name = "E2",
                Slug = "s01e02",
                Ordinal = 2
            },
            new()
            {
                Id = new Guid("00000000-0000-0000-0000-000000000003"),
                ParentId = season.Id,
                ContentType = "tv",
                Level = "episode",
                Name = "E3",
                Slug = "s01e03",
                Ordinal = 3
            }
        ];
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/Season 01/Bluey.S01E01-E03.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Pending,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Bluey",
                    LevelSlug = "show"
                }
            ]
        };

        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);

        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.PreviewImportHierarchyAsync(
                It.IsAny<PreviewByExternalIdRequest>(),
                candidate.RootFolderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([show, season, .. episodes]);

        var fileRouter = new Mock<IFileRoutingContentTypeDefinitionProvider>();
        fileRouter
            .Setup(router => router.ResolveFileChildren(
                It.Is<ContentHierarchyItem>(parent => parent.Id == show.Id),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyDictionary<string, List<string>>>()))
            .Returns([season.Id]);
        fileRouter
            .Setup(router => router.ResolveFileChildren(
                It.Is<ContentHierarchyItem>(parent => parent.Id == season.Id),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyDictionary<string, List<string>>>()))
            .Returns([.. episodes.Select(episode => episode.Id)]);

        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.FindProviderFor("tv")).Returns(fileRouter.Object);
        contentTypeRegistry
            .Setup(registry => registry.GetChildLevels(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([]);

        var contentFileService = new Mock<IContentFileService>();
        IReadOnlyList<ContentHierarchyItem> previewedLineage = [];
        IReadOnlyList<ContentHierarchyItem> previewedCovered = [];
        contentFileService
            .Setup(service => service.HasRenameMaskAsync(candidate.RootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        contentFileService
            .Setup(service => service.PreviewImportRenameMaskAsync(
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                candidate.FilePath,
                It.IsAny<CancellationToken>(),
                null))
            .Callback<IReadOnlyList<ContentHierarchyItem>, IReadOnlyList<ContentHierarchyItem>, string,
                CancellationToken, IReadOnlyList<FileRenameEntry>?>((lineage, covered, _, _, _) =>
            {
                previewedLineage = lineage;
                previewedCovered = covered;
            })
            .ReturnsAsync(new FileRenamePreview
            {
                HasMask = true,
                Files =
                [
                    new FileRenameEntry
                    {
                        OriginalPath = candidate.FilePath,
                        ContentHierarchyItemId = episodes[0].Id,
                        DestinationPath = "/media/tv/Bluey/S01E01-03.mkv"
                    }
                ]
            });

        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            contentTypeRegistry.Object,
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var preview = await subject
            .PreviewRenamesAsync(new LibraryImportRenameSelection { Ids = [candidate.Id] })
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(preview.Files.Single().DestinationPath, Is.EqualTo("/media/tv/Bluey/S01E01-03.mkv"));
            Assert.That(previewedLineage.Select(item => item.Id), Is.EqualTo(
                [
                    new Guid("00000000-0000-0000-0000-000000000001"),
                    new Guid("00000000-0000-0000-0000-00000000b001"),
                    new Guid("00000000-0000-0000-0000-00000000a001")
                ]));
            Assert.That(previewedCovered.Select(item => item.Id), Is.EqualTo(
                [
                    new Guid("00000000-0000-0000-0000-000000000002"),
                    new Guid("00000000-0000-0000-0000-000000000003")
                ]));
        }
    }

    [Test]
    public async Task QueryCandidateGroupsAsync_ReturnsLayoutGroup_WhenCandidatesShareALayout()
    {
        // Arrange
        _definitions["tv"] = new ContentTypeDefinition
        {
            Slug = "tv",
            DisplayName = "TV",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "show", ParentSlug = null, DisplayName = "Show", CustomMetadataFields = []
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "season", ParentSlug = "show", DisplayName = "Season", CustomMetadataFields = []
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "episode", ParentSlug = "season", DisplayName = "Episode", CustomMetadataFields = []
                }
            ]
        };
        _groupSummaries =
        [
            new LibraryImportCandidateGroupSummary
            {
                GroupKey = "/media/tv/Show A",
                Representative = new LibraryImportCandidate
                {
                    RootFolderId = Guid.NewGuid(),
                    ScanRunId = Guid.NewGuid(),
                    FilePath = "/media/tv/Show A/S01E01.mkv",
                    ContentType = "tv",
                    Status = LibraryImportCandidateStatus.Pending,
                    LayoutKey = "/media/tv/Show A",
                    GroupKey = "/media/tv/Show A"
                },
                StatusCounts = new Dictionary<LibraryImportCandidateStatus, int>
                {
                    [LibraryImportCandidateStatus.Pending] = 3
                }
            }
        ];

        // Act
        var page = await _subject.QueryCandidateGroupsAsync(
            new LibraryImportCandidateQuery(),
            new PagingParameter { PageNumber = 1, PageSize = 25 }).ConfigureAwait(false);

        // Assert
        var group = page.Items.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.TotalItems, Is.EqualTo(1));
            Assert.That(group.Title, Is.EqualTo("Show A"));
            Assert.That(group.LeafCount, Is.EqualTo(3));
            Assert.That(group.TopLevelSlug, Is.EqualTo("show"));
            Assert.That(group.LeafLevelSlug, Is.EqualTo("episode"));
            Assert.That(group.LeafLevelDisplayName, Is.EqualTo("Episode"));
            Assert.That(group.ChildLevelSlug, Is.EqualTo("season"));
            Assert.That(group.ChildLevelDisplayName, Is.EqualTo("Season"));
            _candidateRepository.Verify(
                repository => repository.QueryAsync(
                    It.IsAny<LibraryImportCandidateQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task QueryCandidateGroupsAsync_TakesTheTitleFromTheMatch_WhenTheGroupIsMatched()
    {
        // Arrange
        _definitions["movie"] = new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie", ParentSlug = null, DisplayName = "Movie", CustomMetadataFields = []
                }
            ]
        };
        _groupSummaries =
        [
            new LibraryImportCandidateGroupSummary
            {
                GroupKey = "match:movie:provider-a:provider-a.movie:603",
                Representative = new LibraryImportCandidate
                {
                    RootFolderId = Guid.NewGuid(),
                    ScanRunId = Guid.NewGuid(),
                    FilePath = "/media/movies/The.Matrix.1999.mkv",
                    ContentType = "movie",
                    Status = LibraryImportCandidateStatus.Pending,
                    GroupKey = "match:movie:provider-a:provider-a.movie:603",
                    Matches =
                    [
                        new LibraryImportMatch
                        {
                            Confidence = 1,
                            ProviderType = "provider-a",
                            ExternalIdKey = "provider-a.movie",
                            ExternalId = "603",
                            Title = "The Matrix",
                            LevelSlug = "movie"
                        }
                    ]
                },
                StatusCounts = new Dictionary<LibraryImportCandidateStatus, int>
                {
                    [LibraryImportCandidateStatus.Pending] = 2
                }
            }
        ];

        // Act
        var page = await _subject.QueryCandidateGroupsAsync(
            new LibraryImportCandidateQuery(),
            new PagingParameter { PageNumber = 1, PageSize = 25 }).ConfigureAwait(false);

        // Assert
        var group = page.Items.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.TotalItems, Is.EqualTo(1));
            Assert.That(group.Title, Is.EqualTo("The Matrix"));
            Assert.That(group.LeafCount, Is.EqualTo(2));
            Assert.That(group.TopLevelSlug, Is.EqualTo("movie"));
            Assert.That(group.LeafLevelSlug, Is.EqualTo("movie"));
            Assert.That(group.ChildLevelSlug, Is.Null);
            Assert.That(group.ChildLevelDisplayName, Is.Null);
            Assert.That(group.Match, Is.Not.Null);
            Assert.That(group.Match!.ExternalId, Is.EqualTo("603"));
        }
    }

    [Test]
    public async Task QueryCandidateGroupsAsync_TakesTheTitleFromTheParsedSignals_WhenTheGroupIsUnmatched()
    {
        // Arrange
        _definitions["movie"] = new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie", ParentSlug = null, DisplayName = "Movie", CustomMetadataFields = []
                }
            ]
        };
        _groupSummaries =
        [
            new LibraryImportCandidateGroupSummary
            {
                GroupKey = "query:movie:nonexistent film",
                Representative = new LibraryImportCandidate
                {
                    RootFolderId = Guid.NewGuid(),
                    ScanRunId = Guid.NewGuid(),
                    FilePath = "/media/movies/a.mkv",
                    ContentType = "movie",
                    Status = LibraryImportCandidateStatus.Pending,
                    GroupKey = "query:movie:nonexistent film",
                    ParsedSignals = new Dictionary<string, List<string>>
                    {
                        [ParsedFileSignalKeys.TitleToken] = ["Nonexistent Film"]
                    }
                },
                StatusCounts = new Dictionary<LibraryImportCandidateStatus, int>
                {
                    [LibraryImportCandidateStatus.Pending] = 2
                }
            }
        ];

        // Act
        var page = await _subject.QueryCandidateGroupsAsync(
            new LibraryImportCandidateQuery(),
            new PagingParameter { PageNumber = 1, PageSize = 25 }).ConfigureAwait(false);

        // Assert
        var group = page.Items.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.TotalItems, Is.EqualTo(1));
            Assert.That(group.Title, Is.EqualTo("Nonexistent Film"));
            Assert.That(group.LeafCount, Is.EqualTo(2));
            Assert.That(group.Match, Is.Null);
        }
    }

    [Test]
    public async Task ImportCandidateAsync_RecordsExceptionChain_WhenImportFails()
    {
        // Arrange
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/S01E01.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Bluey"
                }
            ]
        };
        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository
            .Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<bool?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("save failed", new Exception("duplicate title")));
        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            Mock.Of<IContentFileService>(),
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id, 0, LibraryImportMonitoringMode.Inherit, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Failed));
            Assert.That(result.Message, Is.EqualTo("Import failed: save failed -> duplicate title"));
            candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                candidate.Id,
                "Import failed: save failed -> duplicate title",
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ImportCandidateAsync_RemovesTheMatch_WhenTheProviderFindsNoRecord()
    {
        // Arrange
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/S01E01.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Importing,
            MatchCount = 1,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "3019",
                    Title = "Bluey"
                }
            ]
        };
        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository
            .Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        candidateRepository
            .Setup(repository => repository.MarkImportFailedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var metadataProvider = new Mock<IContentHierarchyMetadataProvider>();
        metadataProvider.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        metadataProvider.SetupGet(provider => provider.SupportedContentTypes).Returns(["tv"]);
        var subject = new LibraryImportService(
            candidateRepository.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IContentFileService>(),
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [metadataProvider.Object],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id, 0, LibraryImportMonitoringMode.Inherit, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Failed));
            Assert.That(
                result.Message,
                Is.EqualTo("The provider found no match for provider-a=3019. The match was removed."));
            candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                candidate.Id,
                "The provider found no match for provider-a=3019. The match was removed.",
                null,
                It.IsAny<CancellationToken>()), Times.Once);
            candidateRepository.Verify(repository => repository.BulkPatchAsync(
                It.Is<BulkPatch<LibraryImportCandidateQuery, LibraryImportCandidatePatch>>(patch =>
                    patch.Ids!.Count == 1 &&
                    patch.Ids[0] == candidate.Id &&
                    patch.Patch.Matches!.Value.Count == 0),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
            candidateRepository.Verify(repository => repository.UpdateAsync(
                It.IsAny<LibraryImportCandidate>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ImportCandidateAsync_KeepsTheMatch_WhenNoProviderIsRegisteredForTheMatch()
    {
        // Arrange
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/S01E01.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Importing,
            MatchCount = 1,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "3019",
                    Title = "Bluey"
                }
            ]
        };
        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository
            .Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        candidateRepository
            .Setup(repository => repository.MarkImportFailedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var subject = new LibraryImportService(
            candidateRepository.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IContentFileService>(),
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id, 0, LibraryImportMonitoringMode.Inherit, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Failed));
            Assert.That(
                result.Message,
                Is.EqualTo("No provider-a metadata provider is available for content type tv. The match was kept."));
            candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                candidate.Id,
                "No provider-a metadata provider is available for content type tv. The match was kept.",
                null,
                It.IsAny<CancellationToken>()), Times.Once);
            candidateRepository.Verify(repository => repository.BulkPatchAsync(
                It.IsAny<BulkPatch<LibraryImportCandidateQuery, LibraryImportCandidatePatch>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            candidateRepository.Verify(repository => repository.UpdateAsync(
                It.IsAny<LibraryImportCandidate>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(LibraryImportMonitoringMode.Monitored, true)]
    [TestCase(LibraryImportMonitoringMode.Unmonitored, false)]
    [TestCase(LibraryImportMonitoringMode.Inherit, null)]
    public async Task ImportCandidateAsync_PassesMonitoringToHydration_WhenModeSelected(
        LibraryImportMonitoringMode monitoringMode,
        bool? expectedMonitoring)
    {
        // Arrange
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/S01E01.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Bluey"
                }
            ]
        };
        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository
            .Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<bool?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((ContentHierarchyItem Root, IReadOnlyCollection<Guid> CreatedHierarchyItemIds)?)null);
        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            Mock.Of<IContentFileService>(),
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        await subject.ImportCandidateAsync(candidate.Id, 0, monitoringMode, null, null).ConfigureAwait(false);

        // Assert
        contentService.Verify(service => service.HydrateWithDescendantsFromExternalIdAsync(
            "provider-a", "provider-a", "44796", "tv", null, candidate.RootFolderId, expectedMonitoring,
            null, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ImportCandidateAsync_FailsWithoutAttachment_WhenTargetLevelDoesNotExistYet()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var root = new ContentHierarchyItem
        {
            Id = rootId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Name = "Bluey",
            Slug = "provider-a-44796"
        };
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/Season 01/Bluey.S01E01.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Bluey",
                    LevelSlug = "show"
                }
            ]
        };
        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                "provider-a", "provider-a", "44796", "tv", "show", candidate.RootFolderId, null, null, null, null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((root, (IReadOnlyCollection<Guid>)[]));
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == rootId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("tv", "show")).Returns(
            [new() { Slug = "season", ParentSlug = "show", DisplayName = "Season", CustomMetadataFields = [] }]);
        var contentFileService = new Mock<IContentFileService>();
        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Guid>> operation, CancellationToken token) => operation(token));
        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            Mock.Of<IFileSystem>(),
            transactionManager.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id,
            0,
            LibraryImportMonitoringMode.Inherit,
            null,
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Failed));
            Assert.That(result.Message,
                Is.EqualTo("Import failed: The target season level does not exist yet below show."));
            candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                candidate.Id,
                "Import failed: The target season level does not exist yet below show.",
                null,
                It.IsAny<CancellationToken>()), Times.Once);
            candidateRepository.Verify(repository => repository.DeleteAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            contentFileService.Verify(service => service.AttachImportedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase("tv", "show", "season", "2026-03-01T11:31:00Z",
        "The import is waiting until the season level below show 'Root' exists.")]
    [TestCase("music", "artist", "album", "2026-03-01T11:31:00Z",
        "The import is waiting until the album level below artist 'Root' exists.")]
    [TestCase("tv", "show", "season", "2026-01-29T12:00:00Z",
        "The import is waiting until the season level below show 'Root' exists.")]
    public async Task ImportCandidateAsync_WaitsWithoutFailing_WhenPendingTargetHasNoChildLevelYet(
        string contentType,
        string level,
        string childLevel,
        string createdAt,
        string expectedMessage)
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var root = new ContentHierarchyItem
        {
            Id = rootId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = contentType,
            Level = level,
            Name = "Root",
            Slug = "provider-a-44796",
            PendingMetadataRefresh = true,
            CreatedAt = DateTimeOffset.Parse(createdAt, CultureInfo.InvariantCulture).UtcDateTime
        };
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/library/Root/file.mkv",
            ContentType = contentType,
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Root",
                    LevelSlug = level
                }
            ]
        };
        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                "provider-a", "provider-a", "44796", contentType, level, candidate.RootFolderId, null, null, null,
                null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((root, (IReadOnlyCollection<Guid>)[]));
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == rootId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetChildLevels(contentType, level)).Returns(
            [new() { Slug = childLevel, ParentSlug = level, DisplayName = childLevel, CustomMetadataFields = [] }]);
        var contentFileService = new Mock<IContentFileService>();
        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            new FakeTimeProvider(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero)),
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id,
            0,
            LibraryImportMonitoringMode.Inherit,
            null,
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Waiting));
            Assert.That(result.Message, Is.EqualTo(expectedMessage));
            Assert.That(result.ContentHierarchyItemId, Is.EqualTo(rootId));
            candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                candidate.Id, It.IsAny<string>(), null, It.IsAny<CancellationToken>()),
                Times.Never);
            candidateRepository.Verify(repository => repository.DeleteAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            contentFileService.Verify(service => service.AttachImportedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ImportCandidateAsync_WaitsOnTheSeason_WhenTheSeasonBelowTheShowHasNoEpisodeLevelYet()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var root = new ContentHierarchyItem
        {
            Id = rootId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Name = "Bluey",
            Slug = "provider-a-44796"
        };
        var season = new ContentHierarchyItem
        {
            Id = seasonId,
            MetadataContainerId = Guid.NewGuid(),
            ParentId = rootId,
            ContentType = "tv",
            Level = "season",
            Name = "Season 1",
            Slug = "season-1",
            PendingMetadataRefresh = true
        };
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/Season 01/Bluey.S01E01.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Bluey",
                    LevelSlug = "show"
                }
            ]
        };
        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                "provider-a", "provider-a", "44796", "tv", "show", candidate.RootFolderId, null, null, null,
                null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((root, (IReadOnlyCollection<Guid>)[]));
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == rootId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [season], TotalItems = 1 });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == seasonId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        var releaseEvaluationService = new Mock<IReleaseEvaluationService>();
        releaseEvaluationService
            .Setup(service => service.AssignFilesToLeavesAsync(
                It.IsAny<Release>(),
                It.Is<ContentHierarchyItem>(parent => parent.Id == rootId),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release _, ContentHierarchyItem _, IReadOnlyList<ContentHierarchyItem> _,
                    IReadOnlyList<ReleaseFileRef> files, CancellationToken _) =>
                new List<ReleaseFileAssignment>
                    { new() { FileKey = files[0].Key, LeafContentHierarchyItemId = seasonId } });
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry
            .Setup(registry => registry.GetChildLevels("tv", "show"))
            .Returns(
                [new() { Slug = "season", ParentSlug = "show", DisplayName = "Season", CustomMetadataFields = [] }]);
        contentTypeRegistry
            .Setup(registry => registry.GetChildLevels("tv", "season"))
            .Returns(
                [
                    new()
                    {
                        Slug = "episode", ParentSlug = "season", DisplayName = "Episode", CustomMetadataFields = []
                    }
                ]);
        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            Mock.Of<IContentFileService>(),
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            releaseEvaluationService.Object,
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id,
            0,
            LibraryImportMonitoringMode.Inherit,
            null,
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Waiting));
            Assert.That(
                result.Message,
                Is.EqualTo("The import is waiting until the episode level below season 'Season 1' exists."));
            Assert.That(result.ContentHierarchyItemId, Is.EqualTo(seasonId));
        }
    }

    [Test]
    public async Task ImportCandidateAsync_AttachesFileToEpisode_WhenHierarchyIsMaterialized()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var root = new ContentHierarchyItem
        {
            Id = rootId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Name = "Bluey",
            Slug = "provider-a-44796"
        };
        var season = new ContentHierarchyItem
        {
            Id = seasonId,
            MetadataContainerId = Guid.NewGuid(),
            ParentId = rootId,
            ContentType = "tv",
            Level = "season",
            Name = "Season 1",
            Slug = "season-1"
        };
        var episode = new ContentHierarchyItem
        {
            Id = episodeId,
            MetadataContainerId = Guid.NewGuid(),
            ParentId = seasonId,
            ContentType = "tv",
            Level = "episode",
            Name = "Magic Xylophone",
            Slug = "s01e01"
        };
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/Season 01/Bluey.S01E01.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Bluey",
                    LevelSlug = "show"
                }
            ],
            ParsedSignals = new Dictionary<string, List<string>> { [ParsedFileSignalKeys.Title] = ["Bluey"] }
        };

        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);

        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                "provider-a", "provider-a", "44796", "tv", "show", candidate.RootFolderId, false, null, null,
                null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((root, (IReadOnlyCollection<Guid>)[rootId, seasonId, episodeId]));

        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        var monitoringPatches = new List<BulkPatch<ContentHierarchyItemCriteria, ContentHierarchyItemPatch>>();
        hierarchyItems
            .Setup(repository => repository.BulkPatchAsync(
                It.IsAny<BulkPatch<ContentHierarchyItemCriteria, ContentHierarchyItemPatch>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<BulkPatch<ContentHierarchyItemCriteria, ContentHierarchyItemPatch>, Guid?, CancellationToken>(
                (patch, _, _) => monitoringPatches.Add(patch))
            .ReturnsAsync(new BulkResult { Count = 1 });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == rootId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [season], TotalItems = 1 });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == seasonId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [episode], TotalItems = 1 });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(
                    criteria => criteria.ParentId.And.Any(c => c.Value == episodeId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems
            .Setup(repository => repository.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var releaseEvaluationService = new Mock<IReleaseEvaluationService>();
        releaseEvaluationService
            .Setup(service => service.AssignFilesToLeavesAsync(
                It.IsAny<Release>(),
                It.Is<ContentHierarchyItem>(parent => parent.Id == rootId),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release _, ContentHierarchyItem _, IReadOnlyList<ContentHierarchyItem> _,
                    IReadOnlyList<ReleaseFileRef> files, CancellationToken _) =>
                new List<ReleaseFileAssignment>
                    { new() { FileKey = files[0].Key, LeafContentHierarchyItemId = seasonId } });
        releaseEvaluationService
            .Setup(service => service.AssignFilesToLeavesAsync(
                It.IsAny<Release>(),
                It.Is<ContentHierarchyItem>(parent => parent.Id == seasonId),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release _, ContentHierarchyItem _, IReadOnlyList<ContentHierarchyItem> _,
                    IReadOnlyList<ReleaseFileRef> files, CancellationToken _) =>
                new List<ReleaseFileAssignment>
                    { new() { FileKey = files[0].Key, LeafContentHierarchyItemId = episodeId } });

        var contentFileService = new Mock<IContentFileService>();
        var attachedHierarchyItemIds = new List<Guid>();
        contentFileService
            .Setup(service => service.AttachImportedAsync(
                It.IsAny<Guid>(), candidate.FilePath, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, Guid?, CancellationToken>((id, _, _, _) => attachedHierarchyItemIds.Add(id))
            .ReturnsAsync(new AttachContentFileResult
            {
                Result = new ContentFileAttachmentResult
                {
                    MediaFile = new MediaFile
                    {
                        Id = Guid.NewGuid(),
                        ContentItemId = Guid.NewGuid(),
                        FileKind = "video",
                        FilePath = candidate.FilePath
                    }
                },
                FulfillmentChanged = false
            });

        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Guid>> operation, CancellationToken token) => operation(token));

        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(system => system.GetFullPath(It.IsAny<string>())).Returns<string>(path => path);

        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry
            .Setup(registry => registry.GetChildLevels("tv", "show"))
            .Returns(
                [new() { Slug = "season", ParentSlug = "show", DisplayName = "Season", CustomMetadataFields = [] }]);
        contentTypeRegistry
            .Setup(registry => registry.GetChildLevels("tv", "season"))
            .Returns(
                [
                    new()
                    {
                        Slug = "episode", ParentSlug = "season", DisplayName = "Episode", CustomMetadataFields = []
                    }
                ]);
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("tv", "episode")).Returns([]);
        contentTypeRegistry
            .Setup(registry => registry.GetDefinitionFor("tv"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "tv",
                DisplayName = "TV",
                LevelDefinitions =
                [
                    new() { Slug = "show", DisplayName = "Show", CustomMetadataFields = [] },
                    new()
                    {
                        Slug = "season", ParentSlug = "show", DisplayName = "Season", CustomMetadataFields = []
                    },
                    new()
                    {
                        Slug = "episode", ParentSlug = "season", DisplayName = "Episode", CustomMetadataFields = []
                    }
                ]
            });

        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            fileSystem.Object,
            transactionManager.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            releaseEvaluationService.Object,
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id,
            0,
            LibraryImportMonitoringMode.Future,
            null,
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            var rootPatch = monitoringPatches.Single(patch => patch.Filter is null);
            var intermediatePatch = monitoringPatches.Single(
                patch => patch.Patch.Monitoring is { Value: null });
            var futureLeavesPatch = monitoringPatches.Single(
                patch => patch.Filter?.Metadata.And.Any() == true &&
                    patch.Patch.Monitoring?.Value == MonitoringMode.Monitored);
            var publishDateCriterion = futureLeavesPatch.Filter!.Metadata.And.Single().PublishDate.And.Single();
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Completed));
            Assert.That(result.ContentHierarchyItemId, Is.EqualTo(episodeId));
            Assert.That(attachedHierarchyItemIds, Is.EqualTo([episodeId]));
            Assert.That(monitoringPatches, Has.Count.EqualTo(3));
            Assert.That(rootPatch.Ids, Is.EqualTo([rootId]));
            Assert.That(rootPatch.Patch.Monitoring!.Value, Is.EqualTo(MonitoringMode.Monitored));
            Assert.That(intermediatePatch.Filter!.Ids, Is.EquivalentTo([seasonId, episodeId]));
            Assert.That(intermediatePatch.Filter.Level.And.Single().Operator,
                Is.EqualTo(CriteriaValueOperator.NotEquals));
            Assert.That(intermediatePatch.Filter.Level.And.Single().Value, Is.EqualTo("episode"));
            Assert.That(intermediatePatch.Patch.Monitoring!.Value, Is.Null);
            Assert.That(futureLeavesPatch.Filter.Ids, Is.EquivalentTo([rootId, seasonId, episodeId]));
            Assert.That(futureLeavesPatch.Filter.Level.And.Single().Value, Is.EqualTo("episode"));
            Assert.That(publishDateCriterion.Operator, Is.EqualTo(CriteriaValueOperator.GreaterThan));
            Assert.That(publishDateCriterion.Negate, Is.False);
            Assert.That(futureLeavesPatch.Patch.Monitoring!.Value, Is.EqualTo(MonitoringMode.Monitored));
            contentService.Verify(service => service.HydrateWithDescendantsFromExternalIdAsync(
                "provider-a", "provider-a", "44796", "tv", "show", candidate.RootFolderId, false,
                null, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ImportCandidateAsync_AttachesInsideTheTransaction_WhenDiscoveryReplacesAGeneratedPath()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var root = new ContentHierarchyItem
        {
            Id = rootId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Name = "Bluey",
            Slug = "provider-a-44796",
            EffectiveRootFolderId = rootFolderId
        };
        var season = new ContentHierarchyItem
        {
            Id = seasonId,
            MetadataContainerId = Guid.NewGuid(),
            ParentId = rootId,
            ContentType = "tv",
            Level = "season",
            Name = "Season 1",
            Slug = "season-1",
            EffectiveRootFolderId = rootFolderId
        };
        var episode = new ContentHierarchyItem
        {
            Id = episodeId,
            MetadataContainerId = Guid.NewGuid(),
            ParentId = seasonId,
            ContentType = "tv",
            Level = "episode",
            Name = "Magic Xylophone",
            Slug = "s01e01",
            EffectiveRootFolderId = rootFolderId
        };
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/Season 01/Bluey.S01E01.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Bluey",
                    LevelSlug = "show"
                }
            ],
            ParsedSignals = new Dictionary<string, List<string>> { [ParsedFileSignalKeys.Title] = ["Bluey"] }
        };

        var events = new List<string>();

        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);

        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                "provider-a", "provider-a", "44796", "tv", "show", candidate.RootFolderId, false, null, null,
                null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((root, (IReadOnlyCollection<Guid>)[]));

        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == rootId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback<ContentHierarchyItemCriteria, PagingParameter, SortParameter?, CancellationToken>(
                (_, _, _, _) => events.Add("route"))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [season], TotalItems = 1 });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == seasonId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [episode], TotalItems = 1 });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(
                    criteria => criteria.ParentId.And.Any(c => c.Value == episodeId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems
            .Setup(repository => repository.GetByIdAsync(episodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(episode);
        hierarchyItems
            .Setup(repository => repository.GetByIdAsync(seasonId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(season);
        hierarchyItems
            .Setup(repository => repository.GetLineageAsync(episodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([episode, season, root]);
        hierarchyItems
            .Setup(repository => repository.PatchAsync(
                It.IsAny<ContentHierarchyItemPatch>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<ContentHierarchyItemPatch, Guid?, CancellationToken>((_, _, _) => events.Add("path-update"))
            .ReturnsAsync(true);
        hierarchyItems.Setup(repository => repository.LockAsync(rootId, It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("root-lock"));
        hierarchyItems.Setup(repository => repository.LockAsync(seasonId, It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("owner-lock"));

        var releaseEvaluationService = new Mock<IReleaseEvaluationService>();
        releaseEvaluationService
            .Setup(service => service.AssignFilesToLeavesAsync(
                It.IsAny<Release>(),
                It.Is<ContentHierarchyItem>(parent => parent.Id == rootId),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release _, ContentHierarchyItem _, IReadOnlyList<ContentHierarchyItem> _,
                    IReadOnlyList<ReleaseFileRef> files, CancellationToken _) =>
                new List<ReleaseFileAssignment>
                    { new() { FileKey = files[0].Key, LeafContentHierarchyItemId = seasonId } });
        releaseEvaluationService
            .Setup(service => service.AssignFilesToLeavesAsync(
                It.IsAny<Release>(),
                It.Is<ContentHierarchyItem>(parent => parent.Id == seasonId),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release _, ContentHierarchyItem _, IReadOnlyList<ContentHierarchyItem> _,
                    IReadOnlyList<ReleaseFileRef> files, CancellationToken _) =>
                new List<ReleaseFileAssignment>
                    { new() { FileKey = files[0].Key, LeafContentHierarchyItemId = episodeId } });

        var contentFileService = new Mock<IContentFileService>();
        contentFileService
            .Setup(service => service.GetEffectivePathAsync(episodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("/media/tv");
        contentFileService
            .Setup(service => service.AttachImportedAsync(
                episodeId, candidate.FilePath, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, Guid?, CancellationToken>((_, _, _, _) => events.Add("attach"))
            .ReturnsAsync(new AttachContentFileResult
            {
                Result = new ContentFileAttachmentResult
                {
                    MediaFile = new MediaFile
                    {
                        Id = Guid.NewGuid(),
                        ContentItemId = Guid.NewGuid(),
                        FileKind = "video",
                        FilePath = candidate.FilePath
                    }
                },
                FulfillmentChanged = false
            });

        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task<Guid>> operation, CancellationToken token) =>
            {
                events.Add("transaction-begin");
                var result = await operation(token).ConfigureAwait(false);
                events.Add("transaction-commit");
                return result;
            });

        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(system => system.GetFullPath(It.IsAny<string>())).Returns<string>(path => path);

        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry
            .Setup(registry => registry.GetChildLevels("tv", "show"))
            .Returns(
                [new() { Slug = "season", ParentSlug = "show", DisplayName = "Season", CustomMetadataFields = [] }]);
        contentTypeRegistry
            .Setup(registry => registry.GetChildLevels("tv", "season"))
            .Returns(
                [
                    new()
                    {
                        Slug = "episode", ParentSlug = "season", DisplayName = "Episode", CustomMetadataFields = []
                    }
                ]);
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("tv", "episode")).Returns([]);
        contentTypeRegistry
            .Setup(registry => registry.GetDefinitionFor("tv"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "tv",
                DisplayName = "TV",
                LevelDefinitions =
                [
                    new() { Slug = "show", DisplayName = "Show", CustomMetadataFields = [] },
                    new()
                    {
                        Slug = "season", ParentSlug = "show", DisplayName = "Season", CustomMetadataFields = []
                    },
                    new()
                    {
                        Slug = "episode", ParentSlug = "season", DisplayName = "Episode", CustomMetadataFields = []
                    }
                ]
            });

        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            fileSystem.Object,
            transactionManager.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            releaseEvaluationService.Object,
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id,
            0,
            LibraryImportMonitoringMode.Future,
            null,
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Completed));
            Assert.That(result.ContentHierarchyItemId, Is.EqualTo(episodeId));
            Assert.That(
                events,
                Is.EqualTo(
                [
                    "route", "transaction-begin", "root-lock", "owner-lock", "path-update", "attach",
                    "transaction-commit"
                ]));
        }
    }

    [Test]
    public async Task ImportCandidateAsync_AttachesFile_WhenTargetIsGenuineLeaf()
    {
        // Arrange
        var movieId = Guid.NewGuid();
        var movie = new ContentHierarchyItem
        {
            Id = movieId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "movie",
            Level = "movie",
            Name = "Arrival",
            Slug = "provider-b-329865"
        };
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/Arrival.mkv",
            ContentType = "movie",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-b",
                    ExternalIdKey = "provider-b",
                    ExternalId = "329865",
                    Title = "Arrival",
                    LevelSlug = "movie"
                }
            ]
        };
        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                "provider-b", "provider-b", "329865", "movie", "movie", candidate.RootFolderId, null, null,
                null, null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((movie, (IReadOnlyCollection<Guid>)[]));
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == movieId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems.Setup(repository => repository.GetLineageAsync(movieId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("movie", "movie")).Returns([]);
        var contentFileService = new Mock<IContentFileService>();
        contentFileService
            .Setup(service => service.AttachImportedAsync(
                movieId, candidate.FilePath, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachContentFileResult
            {
                Result = new ContentFileAttachmentResult
                {
                    MediaFile = new MediaFile
                    {
                        Id = Guid.NewGuid(),
                        ContentItemId = Guid.NewGuid(),
                        FileKind = "video",
                        FilePath = candidate.FilePath
                    }
                },
                FulfillmentChanged = false
            });
        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Guid>> operation, CancellationToken token) => operation(token));
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(system => system.GetFullPath(It.IsAny<string>())).Returns<string>(path => path);
        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            fileSystem.Object,
            transactionManager.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id,
            0,
            LibraryImportMonitoringMode.Inherit,
            null,
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Completed));
            Assert.That(result.ContentHierarchyItemId, Is.EqualTo(movieId));
            contentFileService.Verify(service => service.AttachImportedAsync(
                movieId, "/media/movies/Arrival.mkv", null, It.IsAny<CancellationToken>()), Times.Once);
            candidateRepository.Verify(repository => repository.DeleteAsync(
                candidate.Id, null, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [TestCase(2, 1)]
    [TestCase(1, 2)]
    public async Task ImportCandidateAsync_AttachesTheFileAndKeepsTheSameNameCopy_WhenTheQualityRanksDiffer(
        int existingRank,
        int importedRank)
    {
        // Arrange
        var movieId = Guid.NewGuid();
        var existing = new MediaFile
        {
            Id = Guid.NewGuid(),
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = "/media/movies/Arrival/Arrival.mkv"
        };
        var movie = new ContentHierarchyItem
        {
            Id = movieId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "movie",
            Level = "movie",
            Name = "Arrival",
            Slug = "provider-b-329865",
            ContentItem = new ContentItem { MediaFiles = { existing } }
        };
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/Arrival/Arrival.mp4",
            ContentType = "movie",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-b",
                    ExternalIdKey = "provider-b",
                    ExternalId = "329865",
                    Title = "Arrival",
                    LevelSlug = "movie"
                }
            ]
        };
        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                "provider-b", "provider-b", "329865", "movie", "movie", candidate.RootFolderId, null, null,
                null, null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((movie, (IReadOnlyCollection<Guid>)[]));
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems.Setup(repository => repository.GetByIdAsync(movieId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(movie);
        hierarchyItems.Setup(repository => repository.GetLineageAsync(movieId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([movie]);
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("movie", "movie")).Returns([]);
        contentTypeRegistry.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    ParentSlug = null,
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"],
                    AcceptedSidecarTypes = [".srt"]
                }
            ]
        });
        var contentFileService = new Mock<IContentFileService>();
        contentFileService
            .Setup(service => service.AttachImportedAsync(
                movieId,
                candidate.FilePath,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachContentFileResult
            {
                Result = new ContentFileAttachmentResult
                {
                    MediaFile = new MediaFile
                    {
                        Id = Guid.NewGuid(),
                        ContentItemId = Guid.NewGuid(),
                        FileKind = "Video",
                        FilePath = candidate.FilePath
                    },
                    IsPrimary = true
                },
                FulfillmentChanged = false
            });
        var releaseEvaluationService = new Mock<IReleaseEvaluationService>();
        releaseEvaluationService
            .Setup(service => service.ScoreFileForContentAsync(existing, movieId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReleaseAssessment
            {
                MatchResult = ReleaseMatchResult.Confident,
                MatchedRankOrdinal = existingRank
            });
        releaseEvaluationService
            .Setup(service => service.ScoreFileForContentAsync(
                It.Is<MediaFile>(file => file.FilePath == "/media/movies/Arrival/Arrival.mp4"),
                movieId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReleaseAssessment
            {
                MatchResult = ReleaseMatchResult.Confident,
                MatchedRankOrdinal = importedRank
            });
        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Guid>> operation, CancellationToken token) => operation(token));
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(system => system.GetFullPath(It.IsAny<string>())).Returns<string>(path => path);
        fileSystem
            .Setup(system => system.LookupMimeTypeAsync(
                "/media/movies/Arrival/Arrival.mp4",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("video/mp4");
        fileSystem.Setup(system => system.ResolveFileKind("video/mp4")).Returns(FileKind.Video);
        fileSystem
            .Setup(system => system.MatchesAnyFileType(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<string>?>()))
            .Returns(true);
        fileSystem.Setup(system => system.PathsEqual(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string left, string right) => string.Equals(left, right, StringComparison.Ordinal));
        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            fileSystem.Object,
            transactionManager.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            releaseEvaluationService.Object,
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id,
            0,
            LibraryImportMonitoringMode.Inherit,
            null,
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Completed));
            contentFileService.Verify(
                service => service.AttachImportedAsync(
                    movieId,
                    "/media/movies/Arrival/Arrival.mp4",
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            contentService.Verify(service => service.UnlinkFileAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            candidateRepository.Verify(
                repository => repository.DeleteAsync(candidate.Id, null, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestCase(2, 1)]
    [TestCase(1, 2)]
    public async Task AssociateCandidateAsync_AttachesTheFileAndKeepsTheSameNameCopy_WhenTheQualityRanksDiffer(
        int existingRank,
        int associatedRank)
    {
        // Arrange
        var movieId = Guid.NewGuid();
        var rootFolderId = Guid.NewGuid();
        var existing = new MediaFile
        {
            Id = Guid.NewGuid(),
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = "/media/movies/Arrival/Arrival.mkv"
        };
        var movie = new ContentHierarchyItem
        {
            Id = movieId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "movie",
            Level = "movie",
            Name = "Arrival",
            Slug = "provider-b-329865",
            EffectiveRootFolderId = rootFolderId,
            ContentItem = new ContentItem { MediaFiles = { existing } }
        };
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/Arrival/Arrival.mp4",
            ContentType = "movie",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-b",
                    ExternalIdKey = "provider-b",
                    ExternalId = "329865",
                    Title = "Arrival",
                    LevelSlug = "movie"
                }
            ]
        };
        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        var contentService = new Mock<IContentService>();
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.FindByExternalIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<(string, string, string, string)>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<(string, string, string, string), Guid>
            {
                [("movie", "movie", "provider-b", "329865")] = movieId
            });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems.Setup(repository => repository.GetByIdAsync(movieId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(movie);
        hierarchyItems.Setup(repository => repository.GetLineageAsync(movieId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([movie]);
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetRootLevelSlug("movie", "movie")).Returns("movie");
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("movie", "movie")).Returns([]);
        contentTypeRegistry.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    ParentSlug = null,
                    DisplayName = "Movie",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["video/*"],
                    AcceptedSidecarTypes = [".srt"]
                }
            ]
        });
        var contentFileService = new Mock<IContentFileService>();
        contentFileService
            .Setup(service => service.AttachImportedAsync(
                movieId, candidate.FilePath, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachContentFileResult
            {
                Result = new ContentFileAttachmentResult
                {
                    MediaFile = new MediaFile
                    {
                        Id = Guid.NewGuid(),
                        ContentItemId = Guid.NewGuid(),
                        FileKind = "Video",
                        FilePath = candidate.FilePath
                    },
                    IsPrimary = true
                },
                FulfillmentChanged = false
            });
        var releaseEvaluationService = new Mock<IReleaseEvaluationService>();
        releaseEvaluationService
            .Setup(service => service.ScoreFileForContentAsync(existing, movieId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReleaseAssessment
            {
                MatchResult = ReleaseMatchResult.Confident,
                MatchedRankOrdinal = existingRank
            });
        releaseEvaluationService
            .Setup(service => service.ScoreFileForContentAsync(
                It.Is<MediaFile>(file => file.FilePath == "/media/movies/Arrival/Arrival.mp4"),
                movieId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReleaseAssessment
            {
                MatchResult = ReleaseMatchResult.Confident,
                MatchedRankOrdinal = associatedRank
            });
        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Guid>> operation, CancellationToken token) => operation(token));
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(system => system.GetFullPath(It.IsAny<string>())).Returns<string>(path => path);
        fileSystem
            .Setup(system => system.LookupMimeTypeAsync(
                "/media/movies/Arrival/Arrival.mp4",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("video/mp4");
        fileSystem.Setup(system => system.ResolveFileKind("video/mp4")).Returns(FileKind.Video);
        fileSystem
            .Setup(system => system.MatchesAnyFileType(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<string>?>()))
            .Returns(true);
        fileSystem.Setup(system => system.PathsEqual(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string left, string right) => string.Equals(left, right, StringComparison.Ordinal));
        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            fileSystem.Object,
            transactionManager.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            releaseEvaluationService.Object,
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.AssociateCandidateAsync(candidate.Id, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Completed));
            contentFileService.Verify(service => service.AttachImportedAsync(
                movieId, "/media/movies/Arrival/Arrival.mp4", null, It.IsAny<CancellationToken>()), Times.Once);
            contentService.Verify(
                service => service.UnlinkFileAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            candidateRepository.Verify(repository => repository.DeleteAsync(
                candidate.Id, null, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [TestCase(LibraryImportMonitoringMode.Monitored)]
    [TestCase(LibraryImportMonitoringMode.Unmonitored)]
    [TestCase(LibraryImportMonitoringMode.Inherit)]
    [TestCase(LibraryImportMonitoringMode.Future)]
    public async Task ImportCandidateAsync_DoesNotPatchRootMonitoring_WhenTheRootAlreadyExisted(
        LibraryImportMonitoringMode monitoringMode)
    {
        // Arrange
        var showId = Guid.NewGuid();
        var show = new ContentHierarchyItem
        {
            Id = showId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Name = "Bluey",
            Slug = "provider-a-44796",
            Monitoring = MonitoringMode.Unmonitored
        };
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/Bluey.S01E01.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Bluey",
                    LevelSlug = "show"
                }
            ]
        };
        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                "provider-a", "provider-a", "44796", "tv", "show", candidate.RootFolderId, It.IsAny<bool?>(),
                null, null,
                null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((show, (IReadOnlyCollection<Guid>)[Guid.NewGuid()]));
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == showId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems.Setup(repository => repository.GetLineageAsync(showId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        hierarchyItems
            .Setup(repository => repository.BulkPatchAsync(
                It.IsAny<BulkPatch<ContentHierarchyItemCriteria, ContentHierarchyItemPatch>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkResult { Count = 1 });
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("tv", "show")).Returns([]);
        var contentFileService = new Mock<IContentFileService>();
        contentFileService
            .Setup(service => service.AttachImportedAsync(
                showId, candidate.FilePath, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachContentFileResult
            {
                Result = new ContentFileAttachmentResult
                {
                    MediaFile = new MediaFile
                    {
                        Id = Guid.NewGuid(),
                        ContentItemId = Guid.NewGuid(),
                        FileKind = "video",
                        FilePath = candidate.FilePath
                    }
                },
                FulfillmentChanged = false
            });
        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Guid>> operation, CancellationToken token) => operation(token));
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(system => system.GetFullPath(It.IsAny<string>())).Returns<string>(path => path);
        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            fileSystem.Object,
            transactionManager.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id,
            0,
            monitoringMode,
            null,
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Completed));
            hierarchyItems.Verify(repository => repository.BulkPatchAsync(
                It.Is<BulkPatch<ContentHierarchyItemCriteria, ContentHierarchyItemPatch>>(patch =>
                    patch.Ids != null && patch.Ids.Contains(showId)),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ImportCandidateAsync_PatchesOnlyTheCreatedLeaf_WhenDescendantsAlreadyExist()
    {
        // Arrange
        using var context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);
        await context.Database.OpenConnectionAsync().ConfigureAwait(false);
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
        var rootContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var unrelatedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var createdContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        context.MetadataContainers.AddRange(rootContainer, unrelatedContainer, createdContainer);
        var root = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = rootContainer,
            ContentType = "tv",
            Level = "show",
            Name = "Bluey",
            Slug = "provider-a-44796",
            Monitoring = MonitoringMode.Unmonitored,
            CreatedAt = DateTime.UtcNow
        };
        var unrelatedShow = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = unrelatedContainer,
            ContentType = "tv",
            Level = "show",
            Name = "Taskmaster",
            Slug = "provider-a-2445",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var createdEpisode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = createdContainer,
            ParentId = root.Id,
            ContentType = "tv",
            Level = "episode",
            Name = "Magic Xylophone",
            Slug = "provider-a-44796-s01e01",
            Monitoring = MonitoringMode.Unmonitored,
            CreatedAt = DateTime.UtcNow
        };
        context.ContentHierarchyItems.AddRange(root, unrelatedShow, createdEpisode);
        await context.SaveChangesAsync().ConfigureAwait(false);
        var transactionManager = new TransactionManager(context, NullLogger<TransactionManager>.Instance);
        var auditRecordRepository = new AuditRecordRepository(
            context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            transactionManager,
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow));
        var metadataContainerRepository = new MetadataContainerRepository(
            context,
            auditRecordRepository,
            new SubjectRepository(
                context,
                auditRecordRepository,
                transactionManager,
                Mock.Of<IEventBus>(),
                NullLogger<SubjectRepository>.Instance,
                timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow)),
            transactionManager,
            NullLogger<MetadataContainerRepository>.Instance,
            timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow));
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("tv", "show")).Returns(
            [new() { Slug = "episode", ParentSlug = "show", DisplayName = "Episode", CustomMetadataFields = [] }]);
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("tv", "episode")).Returns([]);
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/Season 01/Bluey.S01E01.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Bluey",
                    LevelSlug = "show"
                }
            ],
            ParsedSignals = new Dictionary<string, List<string>>
            {
                [ParsedFileSignalKeys.Title] = ["Magic Xylophone"]
            }
        };
        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                "provider-a", "provider-a", "44796", "tv", "show", candidate.RootFolderId, false, null, null,
                null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((root, (IReadOnlyCollection<Guid>)[createdEpisode.Id]));
        var contentFileService = new Mock<IContentFileService>();
        contentFileService
            .Setup(service => service.AttachImportedAsync(
                createdEpisode.Id, candidate.FilePath, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachContentFileResult
            {
                Result = new ContentFileAttachmentResult
                {
                    MediaFile = new MediaFile
                    {
                        Id = Guid.NewGuid(),
                        ContentItemId = Guid.NewGuid(),
                        FileKind = "video",
                        FilePath = candidate.FilePath
                    }
                },
                FulfillmentChanged = false
            });
        var releaseEvaluationService = new Mock<IReleaseEvaluationService>();
        releaseEvaluationService
            .Setup(service => service.AssignFilesToLeavesAsync(
                It.IsAny<Release>(),
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release _, ContentHierarchyItem _, IReadOnlyList<ContentHierarchyItem> _,
                    IReadOnlyList<ReleaseFileRef> files, CancellationToken _) =>
                new List<ReleaseFileAssignment>
                    { new() { FileKey = files[0].Key, LeafContentHierarchyItemId = createdEpisode.Id } });
        var importTransactionManager = new Mock<ITransactionManager>();
        importTransactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Guid>> operation, CancellationToken token) => operation(token));
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(system => system.GetFullPath(It.IsAny<string>())).Returns<string>(path => path);
        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            contentTypeRegistry.Object,
            new ContentHierarchyItemRepository(
                context,
                auditRecordRepository,
                transactionManager,
                metadataContainerRepository,
                contentTypeRegistry.Object,
                Mock.Of<IEventBus>(),
                NullLogger<ContentHierarchyItemRepository>.Instance,
                new FakeTimeProvider(DateTimeOffset.UtcNow), Mock.Of<IContentRepository>()),
            fileSystem.Object,
            importTransactionManager.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            releaseEvaluationService.Object,
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id,
            0,
            LibraryImportMonitoringMode.Future,
            null,
            null).ConfigureAwait(false);

        // Assert
        var stored = await context.ContentHierarchyItems.AsNoTracking().ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Completed));
            Assert.That(
                stored.Single(item => item.Id == unrelatedShow.Id).Monitoring,
                Is.EqualTo(MonitoringMode.Monitored));
            Assert.That(stored.Single(item => item.Id == root.Id).Monitoring, Is.EqualTo(MonitoringMode.Unmonitored));
            Assert.That(
                stored.Single(item => item.Id == createdEpisode.Id).Monitoring,
                Is.EqualTo(MonitoringMode.Unmonitored));
        }
    }

    [TestCase("Choking Victim", "No Gods / No Managers", "Crack Rock Steady", "Money Brain",
        "Choking Victim/No Gods + No Managers/02 - Money Brain.flac", "No Gods + No Managers", false, "album")]
    [TestCase("NOFX", "Self/Entitled", "72 Hookers", "Ronnie & Mags",
        "NOFX/Self+Entitled/NOFX - 02 - Ronnie & Mags.flac", "Self+Entitled", true, "second track")]
    [TestCase("Jeff Rosenstock", "NO DREAM", "Life Admin", "NO DREAM",
        "Jeff Rosenstock/NO DREAM/Jeff Rosenstock - NO DREAM - 14 - -BNB.flac", "NO DREAM", false, "album")]
    [TestCase("Jeff Rosenstock", "NO DREAM", "Life Admin", "NO DREAM",
        "Jeff Rosenstock/NO DREAM/05 - Leave It in the Sun.flac", "NO DREAM", false, "album")]
    public async Task ImportCandidateAsync_BindsATrackOnlyByTheProviderMatch_WhenTheAlbumIsFoundByItsFolder(
        string artistName,
        string albumName,
        string firstTrackName,
        string secondTrackName,
        string relativePath,
        string titleSignal,
        bool providerMatchesTheSecondTrack,
        string expectedItem)
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var firstTrackId = Guid.NewGuid();
        var secondTrackId = Guid.NewGuid();
        var root = new ContentHierarchyItem
        {
            Id = rootId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "music",
            Level = "artist",
            Name = artistName,
            Slug = "provider-a-44796"
        };
        var album = new ContentHierarchyItem
        {
            Id = albumId,
            MetadataContainerId = Guid.NewGuid(),
            ParentId = rootId,
            ContentType = "music",
            Level = "album",
            Name = albumName,
            Slug = "album"
        };
        var firstTrack = new ContentHierarchyItem
        {
            Id = firstTrackId,
            MetadataContainerId = Guid.NewGuid(),
            ParentId = albumId,
            ContentType = "music",
            Level = "track",
            Name = firstTrackName,
            Slug = "first-track",
            Ordinal = 1
        };
        var secondTrack = new ContentHierarchyItem
        {
            Id = secondTrackId,
            MetadataContainerId = Guid.NewGuid(),
            ParentId = albumId,
            ContentType = "music",
            Level = "track",
            Name = secondTrackName,
            Slug = "second-track",
            Ordinal = 2
        };
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/music/" + relativePath,
            ContentType = "music",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = artistName,
                    LevelSlug = "artist"
                }
            ],
            ParsedSignals = new Dictionary<string, List<string>> { [ParsedFileSignalKeys.Title] = [titleSignal] }
        };

        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);

        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                "provider-a", "provider-a", "44796", "music", "artist", candidate.RootFolderId, null, null, null,
                null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((root, (IReadOnlyCollection<Guid>)[]));

        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == rootId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [album], TotalItems = 1 });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == albumId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [firstTrack, secondTrack], TotalItems = 2 });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria =>
                    criteria.ParentId.And.Any(c => c.Value == secondTrackId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems
            .Setup(repository => repository.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var releaseEvaluationService = new Mock<IReleaseEvaluationService>();
        releaseEvaluationService
            .Setup(service => service.AssignFilesToLeavesAsync(
                It.IsAny<Release>(),
                It.Is<ContentHierarchyItem>(parent => parent.Id == rootId),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release _, ContentHierarchyItem _, IReadOnlyList<ContentHierarchyItem> _,
                    IReadOnlyList<ReleaseFileRef> files, CancellationToken _) =>
                new List<ReleaseFileAssignment>
                    { new() { FileKey = files[0].Key, LeafContentHierarchyItemId = null } });
        releaseEvaluationService
            .Setup(service => service.AssignFilesToLeavesAsync(
                It.IsAny<Release>(),
                It.Is<ContentHierarchyItem>(parent => parent.Id == albumId),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release _, ContentHierarchyItem _, IReadOnlyList<ContentHierarchyItem> _,
                    IReadOnlyList<ReleaseFileRef> files, CancellationToken _) =>
                new List<ReleaseFileAssignment>
                    {
                        new()
                        {
                            FileKey = files[0].Key,
                            LeafContentHierarchyItemId = providerMatchesTheSecondTrack ? secondTrackId : null
                        }
                    });

        var contentFileService = new Mock<IContentFileService>();
        var attachedHierarchyItemIds = new List<Guid>();
        contentFileService
            .Setup(service => service.AttachImportedAsync(
                It.IsAny<Guid>(), candidate.FilePath, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, Guid?, CancellationToken>((id, _, _, _) => attachedHierarchyItemIds.Add(id))
            .ReturnsAsync(new AttachContentFileResult
            {
                Result = new ContentFileAttachmentResult
                {
                    MediaFile = new MediaFile
                    {
                        Id = Guid.NewGuid(),
                        ContentItemId = Guid.NewGuid(),
                        FileKind = "audio",
                        FilePath = candidate.FilePath
                    }
                },
                FulfillmentChanged = false
            });

        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Guid>> operation, CancellationToken token) => operation(token));

        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(system => system.GetFullPath(It.IsAny<string>())).Returns<string>(path => path);
        fileSystem
            .Setup(system => system.MatchesAnyFileTypeAsync(
                candidate.FilePath,
                It.Is<IReadOnlyList<string>>(types => types.SequenceEqual(new[] { "audio/*" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("music", "artist")).Returns(
            [new() { Slug = "album", ParentSlug = "artist", DisplayName = "Album", CustomMetadataFields = [] }]);
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("music", "album")).Returns(
            [new() { Slug = "track", ParentSlug = "album", DisplayName = "Track", CustomMetadataFields = [] }]);
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("music", "track")).Returns([]);
        contentTypeRegistry.Setup(registry => registry.GetDefinitionFor("music")).Returns(new ContentTypeDefinition
        {
            Slug = "music",
            DisplayName = "Music",
            LevelDefinitions =
            [
                new() { Slug = "artist", DisplayName = "Artist", CustomMetadataFields = [] },
                new()
                {
                    Slug = "album",
                    ParentSlug = "artist",
                    DisplayName = "Album",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["audio/*"]
                },
                new()
                {
                    Slug = "track",
                    ParentSlug = "album",
                    DisplayName = "Track",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["audio/*"]
                }
            ]
        });

        var auditRecordRepository = new Mock<IAuditRecordRepository> { CallBase = true };
        var auditRecords = new List<AuditRecord>();
        auditRecordRepository
            .Setup(repository => repository.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuditRecord, CancellationToken>((record, _) => auditRecords.Add(record))
            .ReturnsAsync((AuditRecord record, CancellationToken _) => record);

        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            fileSystem.Object,
            transactionManager.Object,
            auditRecordRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            releaseEvaluationService.Object,
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id,
            0,
            LibraryImportMonitoringMode.Inherit,
            null,
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Completed));
            Assert.That(
                attachedHierarchyItemIds,
                Is.EqualTo([expectedItem == "album" ? albumId : secondTrackId]));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ImportCandidateAsync_AttachesTheFileToTheAlbum_WhenTheAlbumAcceptsItAndNoTrackMatches(
        bool albumAcceptsTheFile)
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var root = new ContentHierarchyItem
        {
            Id = rootId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "music",
            Level = "artist",
            Name = "Jeff Rosenstock",
            Slug = "provider-a-44796"
        };
        var album = new ContentHierarchyItem
        {
            Id = albumId,
            MetadataContainerId = Guid.NewGuid(),
            ParentId = rootId,
            ContentType = "music",
            Level = "album",
            Name = "SKA DREAM",
            Slug = "ska-dream"
        };
        var track = new ContentHierarchyItem
        {
            Id = trackId,
            MetadataContainerId = Guid.NewGuid(),
            ParentId = albumId,
            ContentType = "music",
            Level = "track",
            Name = "Life Admin",
            Slug = "life-admin",
            Ordinal = 1
        };
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/music/Jeff Rosenstock/SKA DREAM/Jeff Rosenstock - SKA DREAM - 10 - -SKA.flac",
            ContentType = "music",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Jeff Rosenstock",
                    LevelSlug = "artist"
                }
            ],
            ParsedSignals = new Dictionary<string, List<string>> { [ParsedFileSignalKeys.Title] = ["SKA DREAM"] }
        };

        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);

        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                "provider-a", "provider-a", "44796", "music", "artist", candidate.RootFolderId, null, null, null,
                null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((root, (IReadOnlyCollection<Guid>)[]));

        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == rootId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [album], TotalItems = 1 });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == albumId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [track], TotalItems = 1 });
        hierarchyItems
            .Setup(repository => repository.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var releaseEvaluationService = new Mock<IReleaseEvaluationService>();
        releaseEvaluationService
            .Setup(service => service.AssignFilesToLeavesAsync(
                It.IsAny<Release>(),
                It.Is<ContentHierarchyItem>(parent => parent.Id == rootId),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release _, ContentHierarchyItem _, IReadOnlyList<ContentHierarchyItem> _,
                    IReadOnlyList<ReleaseFileRef> files, CancellationToken _) =>
                new List<ReleaseFileAssignment>
                    { new() { FileKey = files[0].Key, LeafContentHierarchyItemId = albumId } });
        releaseEvaluationService
            .Setup(service => service.AssignFilesToLeavesAsync(
                It.IsAny<Release>(),
                It.Is<ContentHierarchyItem>(parent => parent.Id == albumId),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release _, ContentHierarchyItem _, IReadOnlyList<ContentHierarchyItem> _,
                    IReadOnlyList<ReleaseFileRef> files, CancellationToken _) =>
                new List<ReleaseFileAssignment>
                    { new() { FileKey = files[0].Key, LeafContentHierarchyItemId = null } });

        var contentFileService = new Mock<IContentFileService>();
        var attachedHierarchyItemIds = new List<Guid>();
        contentFileService
            .Setup(service => service.AttachImportedAsync(
                It.IsAny<Guid>(), candidate.FilePath, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, Guid?, CancellationToken>((id, _, _, _) => attachedHierarchyItemIds.Add(id))
            .ReturnsAsync(new AttachContentFileResult
            {
                Result = new ContentFileAttachmentResult
                {
                    MediaFile = new MediaFile
                    {
                        Id = Guid.NewGuid(),
                        ContentItemId = Guid.NewGuid(),
                        FileKind = "audio",
                        FilePath = candidate.FilePath
                    }
                },
                FulfillmentChanged = false
            });

        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Guid>> operation, CancellationToken token) => operation(token));

        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(system => system.GetFullPath(It.IsAny<string>())).Returns<string>(path => path);
        fileSystem
            .Setup(system => system.MatchesAnyFileTypeAsync(
                candidate.FilePath,
                It.Is<IReadOnlyList<string>>(types => types.SequenceEqual(new[] { "audio/*" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(albumAcceptsTheFile);
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("music", "artist")).Returns(
            [new() { Slug = "album", ParentSlug = "artist", DisplayName = "Album", CustomMetadataFields = [] }]);
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("music", "album")).Returns(
            [new() { Slug = "track", ParentSlug = "album", DisplayName = "Track", CustomMetadataFields = [] }]);
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("music", "track")).Returns([]);
        contentTypeRegistry.Setup(registry => registry.GetDefinitionFor("music")).Returns(new ContentTypeDefinition
        {
            Slug = "music",
            DisplayName = "Music",
            LevelDefinitions =
            [
                new() { Slug = "artist", DisplayName = "Artist", CustomMetadataFields = [] },
                new()
                {
                    Slug = "album",
                    ParentSlug = "artist",
                    DisplayName = "Album",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["audio/*"]
                },
                new()
                {
                    Slug = "track",
                    ParentSlug = "album",
                    DisplayName = "Track",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = ["audio/*"]
                }
            ]
        });

        var auditRecordRepository = new Mock<IAuditRecordRepository> { CallBase = true };
        var auditRecords = new List<AuditRecord>();
        auditRecordRepository
            .Setup(repository => repository.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuditRecord, CancellationToken>((record, _) => auditRecords.Add(record))
            .ReturnsAsync((AuditRecord record, CancellationToken _) => record);

        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            fileSystem.Object,
            transactionManager.Object,
            auditRecordRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            releaseEvaluationService.Object,
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id,
            0,
            LibraryImportMonitoringMode.Inherit,
            null,
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Status,
                Is.EqualTo(albumAcceptsTheFile
                    ? LibraryImportCompletionStatus.Completed
                    : LibraryImportCompletionStatus.Failed));
            Assert.That(attachedHierarchyItemIds, Is.EqualTo(albumAcceptsTheFile ? new[] { albumId } : []));
            candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                candidate.Id, It.Is<string>(message => message.Contains("unresolved below album 'SKA DREAM'")), null,
                It.IsAny<CancellationToken>()), albumAcceptsTheFile ? Times.Never() : Times.Once());
        }
    }

    [Test]
    public async Task ImportCandidateAsync_KeepsVisibleFailure_WhenEpisodeCannotBeAligned()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var root = new ContentHierarchyItem
        {
            Id = rootId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Name = "Bluey",
            Slug = "provider-a-44796"
        };
        var season = new ContentHierarchyItem
        {
            Id = seasonId,
            MetadataContainerId = Guid.NewGuid(),
            ParentId = rootId,
            ContentType = "tv",
            Level = "season",
            Name = "Season 1",
            Slug = "season-1"
        };
        var episode = new ContentHierarchyItem
        {
            Id = episodeId,
            MetadataContainerId = Guid.NewGuid(),
            ParentId = seasonId,
            ContentType = "tv",
            Level = "episode",
            Name = "Magic Xylophone",
            Slug = "s01e01"
        };
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/Season 01/Bluey.S01E99.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Bluey",
                    LevelSlug = "show"
                }
            ],
            ParsedSignals = new Dictionary<string, List<string>> { [ParsedFileSignalKeys.Title] = ["Bluey"] }
        };

        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);

        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                "provider-a", "provider-a", "44796", "tv", "show", candidate.RootFolderId, null, null, null, null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((root, (IReadOnlyCollection<Guid>)[]));

        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == rootId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [season], TotalItems = 1 });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == seasonId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [episode], TotalItems = 1 });
        hierarchyItems
            .Setup(repository => repository.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var releaseEvaluationService = new Mock<IReleaseEvaluationService>();
        releaseEvaluationService
            .Setup(service => service.AssignFilesToLeavesAsync(
                It.IsAny<Release>(),
                It.Is<ContentHierarchyItem>(parent => parent.Id == rootId),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release _, ContentHierarchyItem _, IReadOnlyList<ContentHierarchyItem> _,
                    IReadOnlyList<ReleaseFileRef> files, CancellationToken _) =>
                new List<ReleaseFileAssignment>
                    { new() { FileKey = files[0].Key, LeafContentHierarchyItemId = seasonId } });
        releaseEvaluationService
            .Setup(service => service.AssignFilesToLeavesAsync(
                It.IsAny<Release>(),
                It.Is<ContentHierarchyItem>(parent => parent.Id == seasonId),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release _, ContentHierarchyItem _, IReadOnlyList<ContentHierarchyItem> _,
                    IReadOnlyList<ReleaseFileRef> files, CancellationToken _) =>
                new List<ReleaseFileAssignment>
                    { new() { FileKey = files[0].Key, LeafContentHierarchyItemId = null } });

        var contentFileService = new Mock<IContentFileService>();
        var attachedHierarchyItemIds = new List<Guid>();
        contentFileService
            .Setup(service => service.AttachImportedAsync(
                It.IsAny<Guid>(), candidate.FilePath, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, Guid?, CancellationToken>((id, _, _, _) => attachedHierarchyItemIds.Add(id))
            .ReturnsAsync(new AttachContentFileResult
            {
                Result = new ContentFileAttachmentResult
                {
                    MediaFile = new MediaFile
                    {
                        Id = Guid.NewGuid(),
                        ContentItemId = Guid.NewGuid(),
                        FileKind = "video",
                        FilePath = candidate.FilePath
                    }
                },
                FulfillmentChanged = false
            });

        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Guid>> operation, CancellationToken token) => operation(token));

        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(system => system.GetFullPath(It.IsAny<string>())).Returns<string>(path => path);

        var auditRecordRepository = new Mock<IAuditRecordRepository> { CallBase = true };
        var auditRecords = new List<AuditRecord>();
        auditRecordRepository
            .Setup(repository => repository.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuditRecord, CancellationToken>((record, _) => auditRecords.Add(record))
            .ReturnsAsync((AuditRecord record, CancellationToken _) => record);

        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            Mock.Of<IContentTypeRegistry>(registry =>
                registry.GetChildLevels(It.IsAny<string>(), It.IsAny<string>()) ==
                    Array.Empty<ContentHierarchyLevelDefinition>()),
            hierarchyItems.Object,
            fileSystem.Object,
            transactionManager.Object,
            auditRecordRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            releaseEvaluationService.Object,
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id,
            0,
            LibraryImportMonitoringMode.Inherit,
            null,
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Failed));
            Assert.That(result.Message, Does.Contain("assignment is unresolved"));
            Assert.That(result.ContentHierarchyItemId, Is.Null);
            Assert.That(attachedHierarchyItemIds, Is.Empty);
            Assert.That(auditRecords, Is.Empty);
            candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                candidate.Id, It.Is<string>(message => message.Contains("assignment is unresolved")), null,
                It.IsAny<CancellationToken>()), Times.Once);
            candidateRepository.Verify(repository => repository.DeleteAsync(
                candidate.Id, null, It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(
        new[]
        {
            "00000000-0000-0000-0000-000000000034",
            "00000000-0000-0000-0000-000000000035",
            "00000000-0000-0000-0000-000000000036"
        },
        "/media/tv/Bluey/Season 01/Bluey.S01E35.en.srt",
        LibraryImportCompletionStatus.Completed,
        new[] { "00000000-0000-0000-0000-000000000034" },
        new[] { "00000000-0000-0000-0000-000000000035", "00000000-0000-0000-0000-000000000036" })]
    [TestCase(
        new[]
        {
            "00000000-0000-0000-0000-000000000034",
            "00000000-0000-0000-0000-000000000035",
            "00000000-0000-0000-0000-000000000036"
        },
        "/media/tv/Bluey/Season 01/Bluey.S01E35.mkv",
        LibraryImportCompletionStatus.Completed,
        new[] { "00000000-0000-0000-0000-000000000034" },
        new[] { "00000000-0000-0000-0000-000000000036" })]
    [TestCase(
        new[] { "00000000-0000-0000-0000-000000000034" },
        "/media/tv/Bluey/Season 01/Bluey.S01E35.en.srt",
        LibraryImportCompletionStatus.Completed,
        new[] { "00000000-0000-0000-0000-000000000034" },
        new string[0])]
    [TestCase(
        new string[0],
        "/media/tv/Bluey/Season 01/Bluey.S01E35.en.srt",
        LibraryImportCompletionStatus.Failed,
        new string[0],
        new string[0])]
    public async Task ImportCandidateAsync_AttachesTheFileToRoutedEpisodesWithoutAPrimaryFile_WhenTheFileNamesEpisodes(
        string[] routed,
        string episode35File,
        LibraryImportCompletionStatus expectedStatus,
        string[] expectedImported,
        string[] expectedAttached)
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var root = new ContentHierarchyItem
        {
            Id = rootId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Name = "Bluey",
            Slug = "provider-a-44796"
        };
        var season = new ContentHierarchyItem
        {
            Id = seasonId,
            MetadataContainerId = Guid.NewGuid(),
            ParentId = rootId,
            ContentType = "tv",
            Level = "season",
            Name = "Season 1",
            Slug = "season-1",
            Ordinal = 1
        };
        ContentHierarchyItem[] episodes =
        [
            new()
            {
                Id = new Guid("00000000-0000-0000-0000-000000000034"),
                ParentId = seasonId,
                ContentType = "tv",
                Level = "episode",
                Name = "E34",
                Slug = "s01e34",
                Ordinal = 34,
                ContentItem = new() { Id = new Guid("00000000-0000-0000-0000-00000000c034") }
            },
            new()
            {
                Id = new Guid("00000000-0000-0000-0000-000000000035"),
                ParentId = seasonId,
                ContentType = "tv",
                Level = "episode",
                Name = "E35",
                Slug = "s01e35",
                Ordinal = 35,
                ContentItem = new()
                {
                    Id = new Guid("00000000-0000-0000-0000-00000000c035"),
                    MediaFiles =
                    {
                        new MediaFile
                        {
                            ContentItemId = new Guid("00000000-0000-0000-0000-00000000c035"),
                            FileKind = "video",
                            FilePath = episode35File
                        }
                    }
                }
            },
            new()
            {
                Id = new Guid("00000000-0000-0000-0000-000000000036"),
                ParentId = seasonId,
                ContentType = "tv",
                Level = "episode",
                Name = "E36",
                Slug = "s01e36",
                Ordinal = 36,
                ContentItem = new() { Id = new Guid("00000000-0000-0000-0000-00000000c036") }
            }
        ];
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/Season 01/Bluey.S01E34-E36.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Bluey",
                    LevelSlug = "show"
                }
            ],
            ParsedSignals = new Dictionary<string, List<string>> { [ParsedFileSignalKeys.Title] = ["Bluey"] }
        };

        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);

        var contentService = new Mock<IContentService>();
        contentService
            .Setup(service => service.HydrateWithDescendantsFromExternalIdAsync(
                "provider-a", "provider-a", "44796", "tv", "show", candidate.RootFolderId, null, null, null, null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((root, (IReadOnlyCollection<Guid>)[]));

        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == rootId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [season], TotalItems = 1 });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == seasonId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = episodes, TotalItems = 3 });
        hierarchyItems
            .Setup(repository => repository.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        hierarchyItems
            .Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => Array.Find(episodes, episode => episode.Id == id));

        var fileRouter = new Mock<IFileRoutingContentTypeDefinitionProvider>();
        fileRouter
            .Setup(router => router.ResolveFileChildren(
                It.Is<ContentHierarchyItem>(parent => parent.Id == rootId),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyDictionary<string, List<string>>>()))
            .Returns([seasonId]);
        fileRouter
            .Setup(router => router.ResolveFileChildren(
                It.Is<ContentHierarchyItem>(parent => parent.Id == seasonId),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyDictionary<string, List<string>>>()))
            .Returns([.. routed.Select(Guid.Parse)]);

        var attached = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = new Guid("00000000-0000-0000-0000-00000000c034"),
            FileKind = "video",
            FilePath = candidate.FilePath
        };

        var contentFileService = new Mock<IContentFileService>();
        var importedHierarchyItemIds = new List<Guid>();
        var attachedHierarchyItemIds = new List<Guid>();
        contentFileService
            .Setup(service => service.AttachImportedAsync(
                It.IsAny<Guid>(), candidate.FilePath, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, Guid?, CancellationToken>((id, _, _, _) => importedHierarchyItemIds.Add(id))
            .ReturnsAsync(new AttachContentFileResult
            {
                Result = new ContentFileAttachmentResult { MediaFile = attached },
                FulfillmentChanged = false
            });
        contentFileService
            .Setup(service => service.AttachAsync(
                It.IsAny<Guid>(), candidate.FilePath, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, Guid?, CancellationToken>((id, _, _, _) => attachedHierarchyItemIds.Add(id))
            .ReturnsAsync(new AttachContentFileResult());

        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Guid>> operation, CancellationToken token) => operation(token));

        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(system => system.GetFullPath(It.IsAny<string>())).Returns<string>(path => path);
        fileSystem
            .Setup(system => system.MatchesAnyFileType(
                It.Is<string>(path => path.EndsWith(".mkv", StringComparison.Ordinal)),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<string>?>()))
            .Returns(true);

        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.FindProviderFor("tv")).Returns(fileRouter.Object);
        contentTypeRegistry
            .Setup(registry => registry.GetChildLevels(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([]);
        contentTypeRegistry
            .Setup(registry => registry.GetDefinitionFor("tv"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "tv",
                DisplayName = "TV",
                LevelDefinitions =
                [
                    new() { Slug = "show", DisplayName = "Show", CustomMetadataFields = [] },
                    new()
                    {
                        Slug = "season", ParentSlug = "show", DisplayName = "Season", CustomMetadataFields = []
                    },
                    new()
                    {
                        Slug = "episode",
                        ParentSlug = "season",
                        DisplayName = "Episode",
                        CustomMetadataFields = [],
                        AcceptedFileTypes = ["video/*"]
                    }
                ]
            });

        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            fileSystem.Object,
            transactionManager.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id,
            0,
            LibraryImportMonitoringMode.Inherit,
            null,
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(expectedStatus));
            Assert.That(importedHierarchyItemIds, Is.EqualTo(expectedImported.Select(Guid.Parse)));
            Assert.That(attachedHierarchyItemIds, Is.EqualTo(expectedAttached.Select(Guid.Parse)));
        }
    }

    [Test]
    public async Task AcceptGroupAsync_QueuesEveryCandidate_WhenTheGroupIsAccepted()
    {
        // Arrange
        var match = new LibraryImportMatch
        {
            Confidence = 1,
            ProviderType = "provider-a",
            ExternalIdKey = "provider-a",
            ExternalId = "44796",
            Title = "Bluey",
            LevelSlug = "show"
        };
        var candidates = new List<LibraryImportCandidate>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Bluey/S01E01.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                GroupKey = "/media/tv/Bluey",
                Matches = [match]
            },
            new()
            {
                Id = Guid.NewGuid(),
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Bluey/S01E02.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                GroupKey = "/media/tv/Bluey",
                Matches = [match]
            },
            new()
            {
                Id = Guid.NewGuid(),
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Motu/S01E01.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                GroupKey = "/media/tv/Motu",
                Matches = [match]
            }
        };

        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository
            .Setup(repository => repository.QueryIdsAsync(
                It.IsAny<LibraryImportCandidateQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((LibraryImportCandidateQuery query, PagingParameter _, CancellationToken __) =>
            {
                var ids = candidates
                    .Where(candidate => candidate.GroupKey == query.GroupKey)
                    .Select(candidate => candidate.Id)
                    .ToList();
                return new PagedList<Guid> { Items = ids, TotalItems = ids.Count };
            });
        candidateRepository
            .Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => candidates.Find(candidate => candidate.Id == id));
        candidateRepository
            .Setup(repository => repository.TryClaimForImportAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var jobManager = new Mock<IJobManager>();
        jobManager
            .Setup(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetChildLevels(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([]);

        var subject = new LibraryImportService(
            candidateRepository.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IContentFileService>(),
            contentTypeRegistry.Object,
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            jobManager.Object,
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject
            .AcceptGroupAsync(
                new LibraryImportCandidateQuery(),
                "/media/tv/Bluey",
                LibraryImportMonitoringMode.Inherit,
                null,
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Queued, Has.Count.EqualTo(2));
            Assert.That(result.Skipped, Is.Empty);
            candidateRepository.Verify(
                repository => repository.TryClaimForImportAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            jobManager.Verify(
                manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
    }

    [Test]
    public async Task SubmitImportAsync_QueuesMatchedFailedCandidate_WhenFilterGiven()
    {
        // Arrange
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/S01E01.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Failed,
            Matches =
            [
                new()
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Bluey"
                }
            ]
        };
        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository
            .Setup(repository => repository.QueryIdsAsync(
                It.Is<LibraryImportCandidateQuery>(query =>
                    query.Statuses != null &&
                    query.Statuses.Contains(LibraryImportCandidateStatus.Pending) &&
                    query.Statuses.Contains(LibraryImportCandidateStatus.Failed) &&
                    query.HasMatches == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Guid> { Items = [candidate.Id], TotalItems = 1 });
        candidateRepository
            .Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        candidateRepository
            .Setup(repository => repository.TryClaimForImportAsync(
                candidate.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var jobManager = new Mock<IJobManager>();
        jobManager.Setup(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        var subject = new LibraryImportService(
            candidateRepository.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IContentFileService>(),
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            jobManager.Object,
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.SubmitImportAsync(
            new BulkAction<LibraryImportCandidateQuery> { Filter = new() },
            LibraryImportMonitoringMode.Inherit,
            null,
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Queued.Single().CandidateId, Is.EqualTo(candidate.Id));
    }

    [Test]
    public async Task AcceptGroupAsync_ReturnsNull_WhenGroupDoesNotExist()
    {
        // Act
        var result = await _subject
            .AcceptGroupAsync(
                new LibraryImportCandidateQuery(),
                "query:nonexistent",
                LibraryImportMonitoringMode.Inherit,
                null,
                null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task AcceptGroupsAsync_ReadsOnlyTheClaimableCandidateIds_WhenTheFilterSelectsARootFolder()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        var match = new LibraryImportMatch
        {
            Confidence = 1,
            ProviderType = "provider-a",
            ExternalIdKey = "provider-a",
            ExternalId = "44796",
            Title = "Bluey",
            LevelSlug = "show"
        };
        _candidates =
        [
            new()
            {
                Id = new Guid("55555555-5555-5555-5555-555555555555"),
                RootFolderId = rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Bluey/S01E01.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                GroupKey = "/media/tv/Bluey",
                Matches = [match]
            },
            new()
            {
                Id = new Guid("66666666-6666-6666-6666-666666666666"),
                RootFolderId = rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Motu/S01E01.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Queued,
                GroupKey = "/media/tv/Motu",
                Matches = [match]
            }
        ];
        _candidateRepository
            .Setup(repository => repository.TryClaimForImportAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _candidateRepository
            .Setup(repository => repository.QueryIdsAsync(
                It.Is<LibraryImportCandidateQuery>(query => query.RootFolderId == rootFolderId),
                It.IsAny<PagingParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Guid>
            {
                Items = [new Guid("55555555-5555-5555-5555-555555555555")],
                TotalItems = 1
            });

        // Act
        var result = await _subject
            .AcceptGroupsAsync(
                new BulkAction<LibraryImportCandidateQuery>
                {
                    Filter = new LibraryImportCandidateQuery { RootFolderId = rootFolderId }
                },
                LibraryImportMonitoringMode.Inherit,
                null,
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Queued, Has.Count.EqualTo(1));
            Assert.That(
                result.Queued[0].CandidateId,
                Is.EqualTo(new Guid("55555555-5555-5555-5555-555555555555")));
            Assert.That(result.Skipped, Is.Empty);
            _candidateRepository.Verify(
                repository => repository.QueryIdsAsync(
                    It.Is<LibraryImportCandidateQuery>(query =>
                        query.HasMatches == true &&
                        query.Statuses!.Contains(LibraryImportCandidateStatus.Pending) &&
                        query.Statuses!.Contains(LibraryImportCandidateStatus.Failed)),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
            _candidateRepository.Verify(
                repository => repository.QueryAsync(
                    It.IsAny<LibraryImportCandidateQuery>(),
                    It.IsAny<PagingParameter>(),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public void SubmitImportAsync_StopsBeforeTheNextClaim_WhenTheCallerCancels()
    {
        // Arrange
        var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        _candidates =
        [
            new()
            {
                Id = new Guid("77777777-7777-7777-7777-777777777777"),
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Bluey/S01E01.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                GroupKey = "/media/tv/Bluey"
            }
        ];

        // Act
        // Assert
        Assert.ThrowsAsync<OperationCanceledException>(() => _subject.SubmitImportAsync(
            new BulkAction<LibraryImportCandidateQuery>
            {
                Ids = [new Guid("77777777-7777-7777-7777-777777777777")]
            },
            LibraryImportMonitoringMode.Inherit,
            null,
            null,
            cancelled.Token));
        using (Assert.EnterMultipleScope())
        {
            _candidateRepository.Verify(
                repository => repository.TryClaimForImportAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task AcceptGroupsAsync_SelectsNothing_WhenTheActionNamesNoCandidateAndNoFilter()
    {
        // Arrange
        _candidates =
        [
            new()
            {
                Id = new Guid("33333333-3333-3333-3333-333333333333"),
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Bluey/S01E01.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                GroupKey = "/media/tv/Bluey"
            }
        ];

        // Act
        var result = await _subject
            .AcceptGroupsAsync(
                new BulkAction<LibraryImportCandidateQuery>(),
                LibraryImportMonitoringMode.Inherit,
                null,
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Queued, Is.Empty);
            Assert.That(result.Skipped, Is.Empty);
        }
    }

    [Test]
    public async Task AcceptGroupsAsync_SelectsOnlyTheNamedCandidates_WhenTheActionCarriesIds()
    {
        // Arrange
        _candidates =
        [
            new()
            {
                Id = new Guid("33333333-3333-3333-3333-333333333333"),
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Bluey/S01E01.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                GroupKey = "/media/tv/Bluey"
            },
            new()
            {
                Id = new Guid("44444444-4444-4444-4444-444444444444"),
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Motu/S01E01.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                GroupKey = "/media/tv/Motu"
            }
        ];

        // Act
        var result = await _subject
            .AcceptGroupsAsync(
                new BulkAction<LibraryImportCandidateQuery>
                {
                    Ids = [new Guid("33333333-3333-3333-3333-333333333333")]
                },
                LibraryImportMonitoringMode.Inherit,
                null,
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Queued, Is.Empty);
            Assert.That(result.Skipped, Has.Count.EqualTo(1));
            Assert.That(
                result.Skipped[0].CandidateId,
                Is.EqualTo(new Guid("33333333-3333-3333-3333-333333333333")));
        }
    }

    [Test]
    public async Task AcceptGroupsAsync_QueuesOnlyNonExcludedGroups_WhenGroupKeysAreExcluded()
    {
        // Arrange
        var match = new LibraryImportMatch
        {
            Confidence = 1,
            ProviderType = "provider-a",
            ExternalIdKey = "provider-a",
            ExternalId = "44796",
            Title = "Bluey",
            LevelSlug = "show"
        };
        var candidates = new List<LibraryImportCandidate>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Bluey/S01E01.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                GroupKey = "/media/tv/Bluey",
                Matches = [match]
            },
            new()
            {
                Id = Guid.NewGuid(),
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Bluey/S01E02.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                GroupKey = "/media/tv/Bluey",
                Matches = [match]
            },
            new()
            {
                Id = Guid.NewGuid(),
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Motu/S01E01.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                GroupKey = "/media/tv/Motu",
                Matches = [match]
            }
        };

        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository
            .Setup(repository => repository.QueryIdsAsync(
                It.IsAny<LibraryImportCandidateQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((LibraryImportCandidateQuery query, PagingParameter _, CancellationToken __) =>
            {
                var ids = candidates
                    .Where(candidate => query.ExcludeGroupKeys is null ||
                        !query.ExcludeGroupKeys.Contains(candidate.GroupKey!))
                    .Select(candidate => candidate.Id)
                    .ToList();
                return new PagedList<Guid> { Items = ids, TotalItems = ids.Count };
            });
        candidateRepository
            .Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => candidates.Find(candidate => candidate.Id == id));
        candidateRepository
            .Setup(repository => repository.TryClaimForImportAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var jobManager = new Mock<IJobManager>();
        jobManager
            .Setup(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetChildLevels(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([]);

        var subject = new LibraryImportService(
            candidateRepository.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IContentFileService>(),
            contentTypeRegistry.Object,
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            jobManager.Object,
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject
            .AcceptGroupsAsync(
                new BulkAction<LibraryImportCandidateQuery>
                {
                    Filter = new() { ExcludeGroupKeys = ["/media/tv/Motu"] }
                },
                LibraryImportMonitoringMode.Inherit,
                null,
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Queued, Has.Count.EqualTo(2));
            Assert.That(result.Skipped, Is.Empty);
            candidateRepository.Verify(
                repository => repository.TryClaimForImportAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            jobManager.Verify(
                manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
    }

    [Test]
    public async Task AcceptGroupsAsync_SkipsANonPendingCandidate_WhenItsClaimFails()
    {
        // Arrange
        var match = new LibraryImportMatch
        {
            Confidence = 1,
            ProviderType = "provider-a",
            ExternalIdKey = "provider-a",
            ExternalId = "44796",
            Title = "Bluey",
            LevelSlug = "show"
        };
        var candidates = new List<LibraryImportCandidate>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Bluey/S01E01.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Queued,
                GroupKey = "/media/tv/Bluey",
                Matches = [match]
            }
        };

        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository
            .Setup(repository => repository.QueryIdsAsync(
                It.IsAny<LibraryImportCandidateQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Guid>
            {
                Items = [.. candidates.Select(candidate => candidate.Id)],
                TotalItems = candidates.Count
            });
        candidateRepository
            .Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => candidates.Find(candidate => candidate.Id == id));
        candidateRepository
            .Setup(repository => repository.TryClaimForImportAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetChildLevels(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([]);

        var subject = new LibraryImportService(
            candidateRepository.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IContentFileService>(),
            contentTypeRegistry.Object,
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject
            .AcceptGroupsAsync(
                new BulkAction<LibraryImportCandidateQuery> { Filter = new() },
                LibraryImportMonitoringMode.Inherit,
                null,
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Queued, Is.Empty);
            Assert.That(result.Skipped, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task AcceptGroupAsync_SkipsAnUnmatchedCandidate_WhenTheGroupMixesMatchedAndUnmatched()
    {
        // Arrange
        var matchedId = Guid.NewGuid();
        var unmatchedId = Guid.NewGuid();
        var candidates = new List<LibraryImportCandidate>
        {
            new()
            {
                Id = matchedId,
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Bluey/S01E01.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                GroupKey = "/media/tv/Bluey",
                Matches =
                [
                    new LibraryImportMatch
                    {
                        Confidence = 1,
                        ProviderType = "provider-a",
                        ExternalIdKey = "provider-a",
                        ExternalId = "44796",
                        Title = "Bluey",
                        LevelSlug = "show"
                    }
                ]
            },
            new()
            {
                Id = unmatchedId,
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Bluey/S01E02.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                GroupKey = "/media/tv/Bluey",
                Matches = []
            }
        };

        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository
            .Setup(repository => repository.QueryIdsAsync(
                It.IsAny<LibraryImportCandidateQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Guid>
            {
                Items = [.. candidates.Select(candidate => candidate.Id)],
                TotalItems = candidates.Count
            });
        candidateRepository
            .Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => candidates.Find(candidate => candidate.Id == id));
        candidateRepository
            .Setup(repository => repository.TryClaimForImportAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var jobManager = new Mock<IJobManager>();
        jobManager
            .Setup(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetChildLevels(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([]);

        var subject = new LibraryImportService(
            candidateRepository.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IContentFileService>(),
            contentTypeRegistry.Object,
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            jobManager.Object,
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject
            .AcceptGroupAsync(
                new LibraryImportCandidateQuery(),
                "/media/tv/Bluey",
                LibraryImportMonitoringMode.Inherit,
                null,
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Queued.Select(queued => queued.CandidateId), Is.EqualTo([matchedId]));
            Assert.That(result.Skipped.Select(skip => skip.CandidateId), Is.EqualTo([unmatchedId]));
            Assert.That(
                result.Skipped[0].Reason,
                Is.EqualTo("Candidate has no match."));
            candidateRepository.Verify(
                repository => repository.TryClaimForImportAsync(
                    unmatchedId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task QueryCandidatesAsync_PassesTheGroupKeyToTheRepository_WhenGroupKeyIsSet()
    {
        // Arrange
        _candidateRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<LibraryImportCandidateQuery>(query => query.GroupKey == "/media/tv/Show A"),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate>
            {
                Items =
                [
                    new LibraryImportCandidate
                    {
                        Id = Guid.NewGuid(),
                        RootFolderId = Guid.NewGuid(),
                        ScanRunId = Guid.NewGuid(),
                        FilePath = "/media/tv/Show A/S01E01.mkv",
                        ContentType = "tv",
                        Status = LibraryImportCandidateStatus.Pending,
                        GroupKey = "/media/tv/Show A"
                    }
                ],
                TotalItems = 1
            });

        // Act
        var page = await _subject.QueryCandidatesAsync(
            new LibraryImportCandidateQuery { GroupKey = "/media/tv/Show A" },
            new PagingParameter { PageNumber = 1, PageSize = 25 },
            sort: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.TotalItems, Is.EqualTo(1));
            Assert.That(page.Items.Single().FilePath, Is.EqualTo("/media/tv/Show A/S01E01.mkv"));
        }
    }

    [Test]
    public async Task MatchGroupAsync_ReturnsNull_WhenGroupDoesNotExist()
    {
        // Act
        var result = await _subject.MatchGroupAsync(
            new LibraryImportCandidateQuery(),
            "query:nonexistent",
            "provider-a",
            "provider-a",
            "44796",
            "Bluey",
            null,
            null,
            "show",
            null,
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task MatchGroupAsync_PatchesEveryCandidateInOneWrite_WhenGroupExists()
    {
        // Arrange
        var candidates = new List<LibraryImportCandidate>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Bluey/S01E01.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                GroupKey = "query:tv:bluey",
                ParsedSignals = new() { [ParsedFileSignalKeys.RootTitle] = ["Bluey"] }
            },
            new()
            {
                Id = Guid.NewGuid(),
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Bluey/S01E02.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                GroupKey = "query:tv:bluey",
                ParsedSignals = new() { [ParsedFileSignalKeys.RootTitle] = ["Bluey"] }
            }
        };

        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<LibraryImportCandidateQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate> { Items = candidates, TotalItems = candidates.Count });
        candidateRepository
            .Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => candidates.SingleOrDefault(c => c.Id == id));
        candidateRepository
            .Setup(repository => repository.QueryGroupsAsync(
                It.IsAny<LibraryImportCandidateQuery>(), It.IsAny<PagingParameter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new PagedList<LibraryImportCandidateGroupSummary>
            {
                Items = [new()
                {
                    GroupKey = "match:tv:provider-a:provider-a:44796",
                    Representative = candidates[0],
                    StatusCounts = new Dictionary<LibraryImportCandidateStatus, int>
                        { [LibraryImportCandidateStatus.Pending] = 2 }
                }],
                TotalItems = 1
            });
        candidateRepository
            .Setup(repository => repository.UpdateAsync(
                It.IsAny<LibraryImportCandidate>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var patches = new List<BulkPatch<LibraryImportCandidateQuery, LibraryImportCandidatePatch>>();
        candidateRepository
            .Setup(repository => repository.BulkPatchAsync(
                It.IsAny<BulkPatch<LibraryImportCandidateQuery, LibraryImportCandidatePatch>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<BulkPatch<LibraryImportCandidateQuery, LibraryImportCandidatePatch>, Guid?, CancellationToken>(
                (patch, _, _) =>
                {
                    patches.Add(patch);
                    foreach (var candidate in candidates)
                    {
                        candidate.Matches = [.. patch.Patch.Matches!.Value];
                        candidate.MatchCount = candidate.Matches.Count;
                        candidate.GroupKey = "match:tv:provider-a:provider-a:44796";
                    }
                })
            .ReturnsAsync(new BulkResult { Count = 2 });

        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetRootLevels(It.IsAny<string>()))
            .Returns([]);
        contentTypeRegistry.Setup(registry => registry.GetChildLevels(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([]);

        var subject = new LibraryImportService(
            candidateRepository.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IContentFileService>(),
            contentTypeRegistry.Object,
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.MatchGroupAsync(
            new LibraryImportCandidateQuery(),
            "query:tv:bluey",
            "provider-a",
            "provider-a",
            "44796",
            "Bluey",
            null,
            null,
            "show",
            null,
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.LeafCount, Is.EqualTo(2));
            Assert.That(result.GroupKey, Is.EqualTo("match:tv:provider-a:provider-a:44796"));
            Assert.That(result.Match, Is.Not.Null);
            Assert.That(result.Match!.ExternalId, Is.EqualTo("44796"));
            Assert.That(patches, Has.Count.EqualTo(1));
            Assert.That(patches[0].Ids, Is.EquivalentTo(candidates.Select(candidate => candidate.Id)));
            candidateRepository.Verify(
                repository => repository.UpdateAsync(
                    It.IsAny<LibraryImportCandidate>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase(false, 1)]
    [TestCase(true, 0)]
    public async Task AssociateCandidateAsync_AttachesTheFileAndMovesOnlyAnEmptyItemFolder_WhenTheLibraryHasTheItem(
        bool itemHasFiles, int expectedPathUpdates)
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/Inception (2010)/Inception.2010.mkv",
            ContentType = "movie",
            Status = LibraryImportCandidateStatus.Queued,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "27205",
                    Title = "Inception",
                    LevelSlug = "movie"
                }
            ]
        };
        var root = new ContentHierarchyItem
        {
            Id = rootId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "movie",
            Level = "movie",
            Name = "Inception",
            Slug = "inception",
            EffectiveRootFolderId = rootFolderId,
            Path = "Inception",
            Settings = new Dictionary<string, string> { ["content.path.origin"] = "Generated" },
            ContentItem = itemHasFiles
                ? new ContentItem
                {
                    MediaFiles =
                    {
                        new MediaFile
                        {
                            Id = Guid.NewGuid(),
                            FileKind = "video",
                            FilePath = "/media/movies/Inception/Inception.mkv"
                        }
                    }
                }
                : null
        };

        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        candidateRepository
            .Setup(repository => repository.TryBeginImportAsync(
                candidate.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var pathUpdates = new List<ContentHierarchyItemPatch>();
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.PatchAsync(
                It.IsAny<ContentHierarchyItemPatch>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((ContentHierarchyItemPatch patch, Guid? _, CancellationToken _) => pathUpdates.Add(patch))
            .ReturnsAsync(true);
        hierarchyItems
            .Setup(repository => repository.FindByExternalIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<(string, string, string, string)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<(string, string, string, string), Guid>
            {
                [("movie", "movie", "provider-a", "27205")] = rootId
            });
        hierarchyItems.Setup(repository => repository.GetByIdAsync(rootId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(root);
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems
            .Setup(repository => repository.GetLineageAsync(rootId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([root]);

        var contentService = new Mock<IContentService>();
        var contentFileService = new Mock<IContentFileService>();
        contentFileService
            .Setup(service => service.GetEffectivePathAsync(rootId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("/media/movies/Inception");
        contentFileService
            .Setup(service => service.AttachImportedAsync(
                rootId, candidate.FilePath, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachContentFileResult
            {
                Result = new ContentFileAttachmentResult
                {
                    MediaFile = new MediaFile
                    {
                        Id = Guid.NewGuid(),
                        ContentItemId = Guid.NewGuid(),
                        FileKind = "video",
                        FilePath = candidate.FilePath
                    }
                },
                FulfillmentChanged = true
            });

        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Guid>> operation, CancellationToken token) => operation(token));

        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        fileSystem.Setup(system => system.GetFullPath(It.IsAny<string>())).Returns<string>(path => path);
        fileSystem.Setup(system => system.PathsEqual(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string left, string right) => string.Equals(left, right, StringComparison.Ordinal));

        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetRootLevelSlug("movie", "movie")).Returns("movie");
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("movie", "movie")).Returns([]);

        var eventBus = new Mock<IEventBus>();
        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            fileSystem.Object,
            transactionManager.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            eventBus.Object,
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.AssociateCandidateAsync(candidate.Id, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Completed));
            Assert.That(result.ContentHierarchyItemId, Is.EqualTo(rootId));
            contentFileService.Verify(
                service => service.AttachImportedAsync(
                    rootId, candidate.FilePath, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Once);
            contentService.Verify(
                service => service.MaterializeFulfillmentStatusAsync(
                    rootId, It.IsAny<Guid?>(), false, It.IsAny<CancellationToken>()),
                Times.Once);
            contentService.Verify(
                service => service.HydrateWithDescendantsFromExternalIdAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool?>(), It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            candidateRepository.Verify(
                repository => repository.DeleteAsync(candidate.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Once);
            eventBus.Verify(
                bus => bus.PublishAsync(
                    It.Is<MagpieEvent>(magpieEvent => magpieEvent.Name == "content.imported"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.That(pathUpdates, Has.Count.EqualTo(expectedPathUpdates));
            Assert.That(
                pathUpdates.Select(patch => Path.GetFileName(patch.Path!.Value)),
                Is.All.EqualTo("Inception (2010)"));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AssociateCandidateAsync_FailsWithoutCreatingContent_WhenTheRootFolderHasNoMatchedItem(
        bool itemInOtherRootFolder)
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/Inception.2010.mkv",
            ContentType = "movie",
            Status = LibraryImportCandidateStatus.Importing,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "27205",
                    Title = "Inception",
                    LevelSlug = "movie"
                }
            ]
        };

        var candidateRepository = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidateRepository.Setup(repository => repository.GetByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);

        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.FindByExternalIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<(string, string, string, string)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(itemInOtherRootFolder
                ? new Dictionary<(string, string, string, string), Guid>
                {
                    [("movie", "movie", "provider-a", "27205")] = rootId
                }
                : []);
        hierarchyItems.Setup(repository => repository.GetByIdAsync(rootId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = rootId,
                MetadataContainerId = Guid.NewGuid(),
                ContentType = "movie",
                Level = "movie",
                Name = "Inception",
                Slug = "inception",
                EffectiveRootFolderId = Guid.NewGuid()
            });

        var contentService = new Mock<IContentService>();
        var contentFileService = new Mock<IContentFileService>();
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetRootLevelSlug("movie", "movie")).Returns("movie");

        var subject = new LibraryImportService(
            candidateRepository.Object,
            contentService.Object,
            contentFileService.Object,
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.AssociateCandidateAsync(candidate.Id, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Failed));
            Assert.That(result.Message, Does.Contain("No content was created and no file was attached."));
            candidateRepository.Verify(
                repository => repository.MarkImportFailedAsync(
                    candidate.Id, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Once);
            contentService.Verify(
                service => service.HydrateWithDescendantsFromExternalIdAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool?>(), It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            contentFileService.Verify(
                service => service.AttachImportedAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ResolveLibraryTargetAsync_ReturnsNull_WhenTheShowHasNoEpisodeLevelYet()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/Season 01/Bluey.S01E01.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Pending,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Bluey",
                    LevelSlug = "show"
                }
            ]
        };

        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.FindByExternalIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<(string, string, string, string)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<(string, string, string, string), Guid>
            {
                [("tv", "show", "provider-a", "44796")] = rootId
            });
        hierarchyItems.Setup(repository => repository.GetByIdAsync(rootId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = rootId,
                MetadataContainerId = Guid.NewGuid(),
                ContentType = "tv",
                Level = "show",
                Name = "Bluey",
                Slug = "bluey",
                EffectiveRootFolderId = rootFolderId
            });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());

        var contentService = new Mock<IContentService>();
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetRootLevelSlug("tv", "show")).Returns("show");
        contentTypeRegistry
            .Setup(registry => registry.GetChildLevels("tv", "show"))
            .Returns(
                [new() { Slug = "season", ParentSlug = "show", DisplayName = "Season", CustomMetadataFields = [] }]);

        var subject = new LibraryImportService(
            Mock.Of<ILibraryImportCandidateRepository>(),
            contentService.Object,
            Mock.Of<IContentFileService>(),
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var target = await subject.ResolveLibraryTargetAsync(candidate).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(target, Is.Null);
            contentService.Verify(
                service => service.HydrateWithDescendantsFromExternalIdAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool?>(), It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ResolveLibraryTargetAsync_ReturnsNull_WhenRoutingCannotChooseAChildOfTheExistingShow()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Bluey/Bluey.Special.mkv",
            ContentType = "tv",
            Status = LibraryImportCandidateStatus.Pending,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "44796",
                    Title = "Bluey",
                    LevelSlug = "show"
                }
            ]
        };

        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.FindByExternalIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<(string, string, string, string)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<(string, string, string, string), Guid>
            {
                [("tv", "show", "provider-a", "44796")] = rootId
            });
        hierarchyItems.Setup(repository => repository.GetByIdAsync(rootId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = rootId,
                MetadataContainerId = Guid.NewGuid(),
                ContentType = "tv",
                Level = "show",
                Name = "Bluey",
                Slug = "bluey",
                EffectiveRootFolderId = rootFolderId
            });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any(c => c.Value == rootId)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = Guid.NewGuid(),
                        MetadataContainerId = Guid.NewGuid(),
                        ParentId = rootId,
                        ContentType = "tv",
                        Level = "season",
                        Name = "Season 1",
                        Slug = "season-1"
                    },
                    new ContentHierarchyItem
                    {
                        Id = Guid.NewGuid(),
                        MetadataContainerId = Guid.NewGuid(),
                        ParentId = rootId,
                        ContentType = "tv",
                        Level = "season",
                        Name = "Season 2",
                        Slug = "season-2"
                    }
                ],
                TotalItems = 2
            });

        var releaseEvaluationService = new Mock<IReleaseEvaluationService>();
        releaseEvaluationService
            .Setup(service => service.AssignFilesToLeavesAsync(
                It.IsAny<Release>(),
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetRootLevelSlug("tv", "show")).Returns("show");

        var subject = new LibraryImportService(
            Mock.Of<ILibraryImportCandidateRepository>(),
            Mock.Of<IContentService>(),
            Mock.Of<IContentFileService>(),
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            releaseEvaluationService.Object,
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var target = await subject.ResolveLibraryTargetAsync(candidate).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(target, Is.Null);
            releaseEvaluationService.Verify(
                service => service.AssignFilesToLeavesAsync(
                    It.IsAny<Release>(),
                    It.Is<ContentHierarchyItem>(parent => parent.Id == rootId),
                    It.Is<IReadOnlyList<ContentHierarchyItem>>(children => children.Count == 2),
                    It.IsAny<IReadOnlyList<ReleaseFileRef>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task AssociateAsync_QueuesOnlyTargetedCandidatesWithoutRename_WhenCandidatesAreSubmitted()
    {
        // Arrange
        var match = new LibraryImportMatch
        {
            Confidence = 1,
            ProviderType = "provider-a",
            ExternalIdKey = "provider-a",
            ExternalId = "27205",
            Title = "Inception"
        };
        var targeted = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/Inception.mkv",
            ContentType = "movie",
            Status = LibraryImportCandidateStatus.Pending,
            ContentHierarchyItemId = Guid.NewGuid(),
            Matches = [match]
        };
        var untargeted = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = targeted.RootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/Other.mkv",
            ContentType = "movie",
            Status = LibraryImportCandidateStatus.Pending,
            Matches = [match]
        };
        _candidates.AddRange([targeted, untargeted]);
        _candidateRepository
            .Setup(repository => repository.TryClaimForImportAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var requests = new List<JobEnqueueRequest>();
        var jobManager = new Mock<IJobManager>();
        jobManager
            .Setup(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback((JobEnqueueRequest request, CancellationToken _) => requests.Add(request))
            .ReturnsAsync(Guid.NewGuid());
        var subject = new LibraryImportService(
            _candidateRepository.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IContentFileService>(),
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            jobManager.Object,
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.AssociateAsync([targeted.Id, untargeted.Id], null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Queued.Select(queued => queued.CandidateId), Is.EqualTo([targeted.Id]));
            Assert.That(result.Skipped.Select(skipped => skipped.CandidateId), Is.EqualTo([untargeted.Id]));
            Assert.That(
                result.Skipped.Single().Reason,
                Is.EqualTo("Candidate has no existing library item to associate with."));
            Assert.That(requests.Single().JobType, Is.EqualTo("RootFolderContentImportItem"));
            Assert.That(requests.Single().Parameters!["associate"], Is.EqualTo("True"));
            Assert.That(requests.Single().Parameters!.ContainsKey("renamePreview"), Is.False);
            _candidateRepository.Verify(
                repository => repository.TryClaimForImportAsync(
                    untargeted.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public async Task RestoreAsync_AuditsInTheWriteTransactionAndQueuesMatching_WhenACandidateIsRestored(
        bool restored, int expected)
    {
        // Arrange
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/Inception.2010.Extended.mkv",
            ContentType = "movie",
            Status = LibraryImportCandidateStatus.Rejected
        };
        _candidates.Add(candidate);
        _candidateRepository
            .Setup(repository => repository.TryRestoreAsync(
                candidate.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(restored);
        var inTransaction = false;
        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task<bool>> operation, CancellationToken token) =>
            {
                inTransaction = true;
                var result = await operation(token).ConfigureAwait(false);
                inTransaction = false;
                return result;
            });
        var audits = new List<(AuditRecord Record, bool InTransaction)>();
        var auditRecords = new Mock<IAuditRecordRepository>();
        auditRecords
            .Setup(repository => repository.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .Callback((AuditRecord record, CancellationToken _) => audits.Add((record, inTransaction)))
            .ReturnsAsync((AuditRecord record, CancellationToken _) => record);
        var jobManager = new Mock<IJobManager>();
        var subject = new LibraryImportService(
            _candidateRepository.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IContentFileService>(),
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IFileSystem>(),
            transactionManager.Object,
            auditRecords.Object,
            jobManager.Object,
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var count = await subject.RestoreAsync(new() { Ids = [candidate.Id] }, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(count, Is.EqualTo(expected));
            Assert.That(
                audits.Select(audit => (audit.Record.Operation, audit.Record.Detail, audit.InTransaction)).ToList(),
                Has.Count.EqualTo(expected).And.All.EqualTo(
                    ("FileRestored", "Restored matching for Inception.2010.Extended.mkv.", true)));
            jobManager.Verify(
                manager => manager.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(request => request.JobType == "LibraryImportMatchResolution"),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(expected));
        }
    }

    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public async Task RejectAsync_AuditsInTheWriteTransaction_WhenACandidateIsRejected(bool rejected, int expected)
    {
        // Arrange
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/Inception.2010.Extended.mkv",
            ContentType = "movie",
            Status = LibraryImportCandidateStatus.Pending
        };
        _candidates.Add(candidate);
        _candidateRepository
            .Setup(repository => repository.TryRejectAsync(
                candidate.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rejected);
        var inTransaction = false;
        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task<bool>> operation, CancellationToken token) =>
            {
                inTransaction = true;
                var result = await operation(token).ConfigureAwait(false);
                inTransaction = false;
                return result;
            });
        var audits = new List<(AuditRecord Record, bool InTransaction)>();
        var auditRecords = new Mock<IAuditRecordRepository>();
        auditRecords
            .Setup(repository => repository.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .Callback((AuditRecord record, CancellationToken _) => audits.Add((record, inTransaction)))
            .ReturnsAsync((AuditRecord record, CancellationToken _) => record);
        var subject = new LibraryImportService(
            _candidateRepository.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IContentFileService>(),
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IFileSystem>(),
            transactionManager.Object,
            auditRecords.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var count = await subject.RejectAsync(new() { Ids = [candidate.Id] }, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(count, Is.EqualTo(expected));
            Assert.That(
                audits.Select(audit => (audit.Record.Operation, audit.Record.Detail, audit.InTransaction)).ToList(),
                Has.Count.EqualTo(expected).And.All.EqualTo(
                    ("FileRejected", "Rejected the matches of Inception.2010.Extended.mkv.", true)));
        }
    }

    [Test]
    public async Task QueryLibraryTargetCandidatesAsync_SelectsTheItemAndEveryDescendant_WhenTheItemHasChildren()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.QueryIdsAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.AncestorId == itemId),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([childId]);
        var queries = new List<LibraryImportCandidateQuery>();
        _candidateRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<LibraryImportCandidateQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .Callback((LibraryImportCandidateQuery query, PagingParameter _, SortParameter? _, CancellationToken _) =>
                queries.Add(query))
            .ReturnsAsync(new PagedList<LibraryImportCandidate>());
        var subject = new LibraryImportService(
            _candidateRepository.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IContentFileService>(),
            Mock.Of<IContentTypeRegistry>(),
            hierarchyItems.Object,
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        await subject.QueryLibraryTargetCandidatesAsync(itemId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(queries.Single().ContentHierarchyItemIds, Is.EquivalentTo([itemId, childId]));
            Assert.That(
                queries.Single().Statuses,
                Is.EquivalentTo(
                [
                    LibraryImportCandidateStatus.Pending,
                    LibraryImportCandidateStatus.Failed,
                    LibraryImportCandidateStatus.Queued,
                    LibraryImportCandidateStatus.Importing
                ]));
        }
    }

    [TestCase(null, null, false, new[] { "/media/movies/Inception/a.mkv" })]
    [TestCase("/media/movies/Inception/Inception.en.srt", null, false, new[] { "/media/movies/Inception/a.mkv" })]
    [TestCase("/media/movies/Inception/Inception.mkv", null, false, new string[0])]
    [TestCase(null, "/media/movies/Inception/Inception.mkv", false, new string[0])]
    [TestCase(null, null, true, new string[0])]
    public async Task AssociateMissingFilesAsync_QueuesOneFileForEachItem_WhenTheItemHasNoPrimaryFile(
        string? existingFile, string? coveringFile, bool associationInFlight, string[] expectedQueuedFiles)
    {
        // Arrange
        var targetId = Guid.NewGuid();
        var match = new LibraryImportMatch
        {
            Confidence = 1,
            ProviderType = "provider-a",
            ExternalIdKey = "provider-a",
            ExternalId = "27205",
            Title = "Inception"
        };
        var second = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/Inception/b.mkv",
            ContentType = "movie",
            Status = LibraryImportCandidateStatus.Pending,
            ContentHierarchyItemId = targetId,
            Matches = [match]
        };
        var first = second with { Id = Guid.NewGuid(), FilePath = "/media/movies/Inception/a.mkv" };
        _candidates.AddRange([second, first]);
        _candidateRepository
            .Setup(repository => repository.TryClaimForImportAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _candidateRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<LibraryImportCandidateQuery>(query => query.ContentHierarchyItemIds != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate>
            {
                Items = associationInFlight
                    ? [second with { Id = Guid.NewGuid(), Status = LibraryImportCandidateStatus.Queued }]
                    : [],
                TotalItems = associationInFlight ? 1 : 0
            });
        var item = new ContentHierarchyItem
        {
            Id = targetId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "movie",
            Level = "movie",
            Name = "Inception",
            Slug = "inception",
            ContentItem = new ContentItem()
        };
        if (existingFile is not null)
        {
            item.ContentItem.MediaFiles.Add(new MediaFile
            {
                FileKind = "video",
                FilePath = existingFile
            });
        }

        if (coveringFile is not null)
        {
            item.ContentItem.CoveringMediaFiles.Add(new MediaFile
            {
                FileKind = "video",
                FilePath = coveringFile
            });
        }

        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems.Setup(repository => repository.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetDefinitionFor("movie")).Returns(new ContentTypeDefinition
        {
            Slug = "movie",
            DisplayName = "Movie",
            LevelDefinitions =
            [
                new()
                {
                    Slug = "movie", DisplayName = "Movie", CustomMetadataFields = [],
                    AcceptedFileTypes = [".mkv"], AcceptedSidecarTypes = [".srt"]
                }
            ]
        });
        var fileSystem = new Mock<IFileSystem>();
        fileSystem
            .Setup(system => system.MatchesAnyFileType(
                "/media/movies/Inception/Inception.mkv", null, It.IsAny<IReadOnlyList<string>?>()))
            .Returns(true);
        var requests = new List<JobEnqueueRequest>();
        var jobManager = new Mock<IJobManager>();
        jobManager
            .Setup(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback((JobEnqueueRequest request, CancellationToken _) => requests.Add(request))
            .ReturnsAsync(Guid.NewGuid());
        var subject = new LibraryImportService(
            _candidateRepository.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IContentFileService>(),
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            fileSystem.Object,
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            jobManager.Object,
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.AssociateMissingFilesAsync([second, first], null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Queued.Select(queued => _candidates.Single(candidate => candidate.Id == queued.CandidateId)
                    .FilePath),
                Is.EqualTo(expectedQueuedFiles));
            Assert.That(requests.Select(request => request.Parameters!["associate"]),
                Is.All.EqualTo("True"));
        }
    }

    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public async Task RefreshLibraryTargetsAsync_SavesTheItem_WhenTheResolvedItemChanged(
        bool itemAdded, int expectedSaves)
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/Inception.2010.mkv",
            ContentType = "movie",
            Status = LibraryImportCandidateStatus.Pending,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "27205",
                    Title = "Inception",
                    LevelSlug = "movie"
                }
            ]
        };
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.FindByExternalIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<(string, string, string, string)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(itemAdded
                ? new Dictionary<(string, string, string, string), Guid>
                {
                    [("movie", "movie", "provider-a", "27205")] = rootId
                }
                : []);
        hierarchyItems.Setup(repository => repository.GetByIdAsync(rootId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = rootId,
                MetadataContainerId = Guid.NewGuid(),
                ContentType = "movie",
                Level = "movie",
                Name = "Inception",
                Slug = "inception",
                EffectiveRootFolderId = rootFolderId
            });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetRootLevelSlug("movie", "movie")).Returns("movie");
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("movie", "movie")).Returns([]);
        _candidateRepository
            .Setup(repository => repository.TryUpdateTargetAsync(
                candidate.Id, rootId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var subject = new LibraryImportService(
            _candidateRepository.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IContentFileService>(),
            contentTypeRegistry.Object,
            hierarchyItems.Object,
            Mock.Of<IFileSystem>(),
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            Mock.Of<IRootFolderRepository>(),
            NullLogger<LibraryImportService>.Instance);

        // Act
        var changed = await subject.RefreshLibraryTargetsAsync([candidate], null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed.Select(item => item.ContentHierarchyItemId).ToList(), Has.Count.EqualTo(expectedSaves)
                .And.All.EqualTo(rootId));
            _candidateRepository.Verify(
                repository => repository.TryUpdateTargetAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Exactly(expectedSaves));
        }
    }
}
