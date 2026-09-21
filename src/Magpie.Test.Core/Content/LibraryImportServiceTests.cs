using System;
using System.Collections.Generic;
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

        _subject = new LibraryImportService(
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
            NullLogger<LibraryImportService>.Instance);
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
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id, 0, LibraryImportMonitoringMode.Inherit, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Failed));
            Assert.That(result.Message, Is.EqualTo(
                "The provider found no match for provider-a=3019. The match was removed. " +
                "Match the file again before you import it."));
            candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                candidate.Id,
                "The provider found no match for provider-a=3019. The match was removed. " +
                    "Match the file again before you import it.",
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
            NullLogger<LibraryImportService>.Instance);

        // Act
        var result = await subject.ImportCandidateAsync(
            candidate.Id, 0, LibraryImportMonitoringMode.Inherit, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Failed));
            Assert.That(result.Message, Is.EqualTo(
                "No provider-a metadata provider is available for content type tv. The match was kept. " +
                "Import the file again when the provider is available."));
            candidateRepository.Verify(repository => repository.MarkImportFailedAsync(
                candidate.Id,
                "No provider-a metadata provider is available for content type tv. The match was kept. " +
                    "Import the file again when the provider is available.",
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
                It.IsAny<Func<CancellationToken, Task<Guid>>>(), It.IsAny<CancellationToken>()))
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
                It.IsAny<Func<CancellationToken, Task<Guid>>>(), It.IsAny<CancellationToken>()))
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
                patch => patch.Filter?.Metadata.And.Any() == true && patch.Patch.Monitoring?.Value == true);
            var publishDateCriterion = futureLeavesPatch.Filter!.Metadata.And.Single().PublishDate.And.Single();
            Assert.That(result.Status, Is.EqualTo(LibraryImportCompletionStatus.Completed));
            Assert.That(result.ContentHierarchyItemId, Is.EqualTo(episodeId));
            Assert.That(attachedHierarchyItemIds, Is.EqualTo([episodeId]));
            Assert.That(monitoringPatches, Has.Count.EqualTo(3));
            Assert.That(rootPatch.Ids, Is.EqualTo([rootId]));
            Assert.That(rootPatch.Patch.Monitoring!.Value, Is.True);
            Assert.That(intermediatePatch.Filter!.Ids, Is.EquivalentTo([seasonId, episodeId]));
            Assert.That(intermediatePatch.Filter.Level.And.Single().Operator,
                Is.EqualTo(CriteriaValueOperator.NotEquals));
            Assert.That(intermediatePatch.Filter.Level.And.Single().Value, Is.EqualTo("episode"));
            Assert.That(intermediatePatch.Patch.Monitoring!.Value, Is.Null);
            Assert.That(futureLeavesPatch.Filter.Ids, Is.EquivalentTo([rootId, seasonId, episodeId]));
            Assert.That(futureLeavesPatch.Filter.Level.And.Single().Value, Is.EqualTo("episode"));
            Assert.That(publishDateCriterion.Operator, Is.EqualTo(CriteriaValueOperator.GreaterThan));
            Assert.That(publishDateCriterion.Negate, Is.False);
            Assert.That(futureLeavesPatch.Patch.Monitoring!.Value, Is.True);
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
                It.IsAny<Func<CancellationToken, Task<Guid>>>(), It.IsAny<CancellationToken>()))
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
                    "route", "transaction-begin", "path-update", "attach", "transaction-commit"
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
                It.IsAny<Func<CancellationToken, Task<Guid>>>(), It.IsAny<CancellationToken>()))
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
            Monitoring = false
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
                It.IsAny<Func<CancellationToken, Task<Guid>>>(), It.IsAny<CancellationToken>()))
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
            Monitoring = false,
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
            Monitoring = true,
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
            Monitoring = false,
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
            .ReturnsAsync([]);
        var importTransactionManager = new Mock<ITransactionManager>();
        importTransactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(), It.IsAny<CancellationToken>()))
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
                new FakeTimeProvider(DateTimeOffset.UtcNow)),
            fileSystem.Object,
            importTransactionManager.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            releaseEvaluationService.Object,
            [],
            TimeProvider.System,
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
            Assert.That(stored.Single(item => item.Id == unrelatedShow.Id).Monitoring, Is.True);
            Assert.That(stored.Single(item => item.Id == root.Id).Monitoring, Is.False);
            Assert.That(stored.Single(item => item.Id == createdEpisode.Id).Monitoring, Is.False);
        }
    }

    [Test]
    public async Task ImportCandidateAsync_AttachesFileToSeason_WhenEpisodeCannotBeAligned()
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
                It.IsAny<Func<CancellationToken, Task<Guid>>>(), It.IsAny<CancellationToken>()))
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
            Mock.Of<IContentTypeRegistry>(),
            hierarchyItems.Object,
            fileSystem.Object,
            transactionManager.Object,
            auditRecordRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            releaseEvaluationService.Object,
            [],
            TimeProvider.System,
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
            Assert.That(result.ContentHierarchyItemId, Is.EqualTo(seasonId));
            Assert.That(attachedHierarchyItemIds, Is.EqualTo([seasonId]));
            var fileImportedRecord = auditRecords.Single(record => record.Operation == "FileImported");
            Assert.That(fileImportedRecord.Detail, Does.Contain("could not be aligned"));
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
        _candidateRepository.Verify(
            repository => repository.TryClaimForImportAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
                Is.EqualTo("Candidate has no match. Match it before you import it."));
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
}
