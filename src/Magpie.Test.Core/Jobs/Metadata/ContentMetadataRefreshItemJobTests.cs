using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs.Metadata;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Content;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Metadata;

[TestFixture]
public sealed class ContentMetadataRefreshItemJobTests
{
    private readonly IProgressReporter _progressReporter = Mock.Of<IProgressReporter>();

    private const string ProviderType = "provider-a";

    private Guid _itemId;
    private Guid _containerId;
    private JobContext _jobContext = null!;
    private SubjectRelationshipDefinition _credit = null!;
    private Mock<IContentHierarchyItemRepository> _contentRepository = null!;
    private Mock<IMetadataContainerRepository> _containerRepository = null!;
    private Mock<ISubjectRepository> _subjectRepository = null!;
    private Mock<IContentTypeRegistry> _contentTypeRegistry = null!;
    private Mock<ISubjectKindRegistry> _subjectKindRegistry = null!;
    private Mock<ITransactionManager> _transactionManager = null!;
    private Mock<IJobManager> _jobManager = null!;
    private Mock<IEventBus> _eventBus = null!;

    public static IEnumerable<TestCaseData> RefreshFailures()
    {
        yield return new TestCaseData(
            new InvalidOperationException("The store is unavailable."),
            CancellationToken.None);
        yield return new TestCaseData(
            new OperationCanceledException(new CancellationToken(true)),
            new CancellationToken(true));
    }

    public static IEnumerable<TestCaseData> WaitingImports()
    {
        yield return new TestCaseData(
            new LibraryImportCandidate[]
            {
                new()
                {
                    RootFolderId = Guid.Empty,
                    ScanRunId = Guid.Empty,
                    FilePath = "/media/movies/The Matrix/The.Matrix.mkv",
                    ContentType = "movie",
                    Status = LibraryImportCandidateStatus.Importing
                }
            },
            1);
        yield return new TestCaseData(Array.Empty<LibraryImportCandidate>(), 0);
    }

    [SetUp]
    public void SetUp()
    {
        _itemId = Guid.NewGuid();
        _containerId = Guid.NewGuid();
        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["contentHierarchyItemId"] = _itemId.ToString("D") },
            Event: null);
        _credit = new SubjectRelationshipDefinition
        {
            RelationshipKind = "cast",
            RelationshipQualifier = "Trinity",
            SubjectKind = "person",
            SubjectName = "Carrie-Anne Moss"
        };

        _contentRepository = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        _contentRepository.Setup(repository => repository.GetLineageAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _contentRepository
            .Setup(repository => repository.GetByIdAsync(_itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = _itemId,
                MetadataContainerId = _containerId,
                ContentType = "movie",
                Level = "movie",
                Slug = "the-matrix",
                Name = "The Matrix",
                MetadataContainer = new MetadataContainer { Id = _containerId }
            });
        _contentRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        _contentRepository
            .Setup(repository => repository.UpdateAsync(
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _containerRepository = new Mock<IMetadataContainerRepository> { CallBase = true };
        _containerRepository
            .Setup(repository => repository.AddContributionAsync(
                It.IsAny<Guid>(),
                It.IsAny<MetadataContribution>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, MetadataContribution contribution, Guid? _, CancellationToken _) => contribution);
        _containerRepository
            .Setup(repository => repository.ListRelationshipKeysAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _subjectRepository = new Mock<ISubjectRepository> { CallBase = true };
        _subjectRepository.Setup(repository => repository.ListIdsByExternalIdentityAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyCollection<ExternalId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _subjectRepository.Setup(repository => repository.ListIdentityDetailsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _subjectRepository
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<Subject>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _subjectRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<SubjectQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject>());

        _contentTypeRegistry = new Mock<IContentTypeRegistry>();
        _contentTypeRegistry
            .Setup(registry => registry.GetDefinitionFor("movie"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "movie",
                DisplayName = "Movie",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "season",
                        ParentSlug = "movie",
                        DisplayName = "Season",
                        CustomMetadataFields = [],
                        Subfolder = string.Empty
                    },
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "episode",
                        ParentSlug = "season",
                        DisplayName = "Episode",
                        CustomMetadataFields = [],
                    }
                ]
            });
        _contentTypeRegistry
            .Setup(registry => registry.GetChildLevels("movie", "movie"))
            .Returns(
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "season",
                    ParentSlug = "movie",
                    DisplayName = "Season",
                    CustomMetadataFields = [],
                    Subfolder = string.Empty
                }
            ]);
        _contentTypeRegistry
            .Setup(registry => registry.GetChildLevels("movie", "season"))
            .Returns(
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "episode",
                    ParentSlug = "season",
                    DisplayName = "Episode",
                    CustomMetadataFields = [],
                }
            ]);

        _subjectKindRegistry = new Mock<ISubjectKindRegistry>();
        _subjectKindRegistry.SetupGet(registry => registry.Definitions).Returns(
        [
            new SubjectKindDefinition
            {
                Slug = "person",
                CustomMetadataFields = []
            }
        ]);

        _transactionManager = new Mock<ITransactionManager>();
        _transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken token) => operation(token));

        _jobManager = new Mock<IJobManager>();
        _eventBus = new Mock<IEventBus>();
    }

    [Test]
    public async Task ExecuteAsync_LinksTheCreditOnce_WhenTheProviderNamesTheRelationship()
    {
        // Arrange
        var provider = new Mock<IContentHierarchyMetadataRelationshipProvider>();
        provider.Setup(p => p.ProviderType).Returns(ProviderType);
        provider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" }
            });
        provider
            .Setup(p => p.GetRelationshipsAsync(
                It.IsAny<MetadataRelationshipRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([_credit]);

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            provider.Verify(
                p => p.GetRelationshipsAsync(
                    It.IsAny<MetadataRelationshipRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _subjectRepository.Verify(
                repository => repository.TryCreateAsync(
                    It.IsAny<Subject>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _containerRepository.Verify(
                repository => repository.AddRelationshipAsync(
                    It.IsAny<Relationship>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_SavesHealthyMetadata_WhenAnotherProviderFails()
    {
        // Arrange
        var healthy = new Mock<IContentHierarchyMetadataProvider>();
        healthy.SetupGet(provider => provider.ProviderType).Returns("healthy");
        healthy.SetupGet(provider => provider.SupportedContentTypes).Returns(["movie"]);
        healthy.Setup(provider => provider.TryGetMetadataAsync(
                It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = "healthy", Title = "The Matrix" }
            });
        var failing = new Mock<IContentHierarchyMetadataProvider>();
        failing.SetupGet(provider => provider.ProviderType).Returns("failing");
        failing.SetupGet(provider => provider.SupportedContentTypes).Returns(["movie"]);
        failing.Setup(provider => provider.TryGetMetadataAsync(
                It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Provider unavailable."));
        var subject = new ContentMetadataRefreshItemJob(
            [healthy.Object, failing.Object], _contentRepository.Object, _containerRepository.Object,
            _subjectRepository.Object, _eventBus.Object, _contentTypeRegistry.Object, _transactionManager.Object,
            _jobManager.Object, Mock.Of<ILibraryImportCandidateRepository>(), _subjectKindRegistry.Object,
            TimeProvider.System, NullLogger<ContentMetadataRefreshItemJob>.Instance, _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _containerRepository.Verify(repository => repository.AddContributionAsync(_containerId,
                It.Is<MetadataContribution>(value => value.ProviderType == "healthy" && value.Title == "The Matrix"),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
            _eventBus.Verify(bus => bus.PublishAsync(
                It.Is<MagpieEvent>(value => value.Name == "metadata.refreshed"),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_LinksBothCredits_WhenTwoPeopleWithoutExternalIdsShareEveryCreditField()
    {
        // Arrange
        var provider = new Mock<IContentHierarchyMetadataRelationshipProvider>();
        provider.Setup(p => p.ProviderType).Returns(ProviderType);
        provider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" }
            });
        provider
            .Setup(p => p.GetRelationshipsAsync(
                It.IsAny<MetadataRelationshipRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "Trinity",
                    SubjectKind = "person",
                    SubjectName = "Carrie-Anne Moss"
                },
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "Trinity",
                    SubjectKind = "person",
                    SubjectName = "Carrie-Anne Moss"
                }
            ]);

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _subjectRepository.Verify(
                repository => repository.TryCreateAsync(
                    It.IsAny<Subject>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            _containerRepository.Verify(
                repository => repository.AddRelationshipAsync(
                    It.IsAny<Relationship>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
    }

    [TestCase(
        MetadataField.Title | MetadataField.BirthDate,
        1,
        1,
        null,
        null,
        1,
        0,
        TestName = "ExecuteAsync_UsesExistingSubject_WhenFallbackIdentityIsComplete")]
    [TestCase(
        MetadataField.Title | MetadataField.BirthDate,
        0,
        1,
        null,
        null,
        2,
        0,
        TestName = "ExecuteAsync_CreatesSubject_WhenFallbackIdentityIsMissing")]
    [TestCase(
        MetadataField.Title | MetadataField.OriginalTitle,
        0,
        0,
        null,
        null,
        2,
        0,
        TestName = "ExecuteAsync_CreatesSubject_WhenNameIsTheOnlyFallbackField")]
    [TestCase(
        MetadataField.BirthDate,
        1,
        1,
        null,
        null,
        1,
        0,
        TestName = "ExecuteAsync_UsesExistingSubject_WhenFallbackIdentityHasOneNonNameField")]
    [TestCase(
        MetadataField.Title | MetadataField.BirthDate,
        1,
        2,
        "shared",
        "shared",
        1,
        0,
        TestName = "ExecuteAsync_UsesExistingSubject_WhenExternalIdMatches")]
    [TestCase(
        MetadataField.Title | MetadataField.BirthDate,
        1,
        1,
        "first",
        "second",
        2,
        0,
        TestName = "ExecuteAsync_CreatesSubject_WhenExternalIdsConflict")]
    [TestCase(
        MetadataField.Title | MetadataField.BirthDate | MetadataField.ExternalIds,
        1,
        1,
        null,
        null,
        2,
        1,
        TestName = "ExecuteAsync_CreatesSubjectAndWarns_WhenFallbackIdentityFieldIsUnsupported")]
    public async Task ExecuteAsync_UsesExistingOrCreatesSubject_WhenFallbackIdentityVaries(
        MetadataField fallbackFields,
        int firstBirthDay,
        int secondBirthDay,
        string? firstExternalId,
        string? secondExternalId,
        int expectedSubjectCount,
        int expectedWarningCount)
    {
        // Arrange
        _subjectKindRegistry.SetupGet(registry => registry.Definitions).Returns(
        [
            new SubjectKindDefinition
            {
                Slug = "person",
                CustomMetadataFields = [],
                FallbackIdentityFields = fallbackFields
            }
        ]);
        var provider = new Mock<IContentHierarchyMetadataRelationshipProvider>();
        provider.Setup(p => p.ProviderType).Returns(ProviderType);
        provider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" }
            });
        provider
            .Setup(p => p.GetRelationshipsAsync(
                It.IsAny<MetadataRelationshipRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "first",
                    SubjectKind = "person",
                    SubjectName = "Carrie-Anne Moss",
                    SubjectExternalIds = firstExternalId is null
                        ? new Dictionary<string, string>()
                        : new Dictionary<string, string> { ["catalog"] = firstExternalId },
                    InitialSubjectContribution = new MetadataContribution
                    {
                        ProviderType = ProviderType,
                        Title = "Carrie-Anne Moss",
                        OriginalTitle = "Carrie-Anne Moss",
                        BirthDate = firstBirthDay == 0 ? null : new DateTime(1967, 1, firstBirthDay)
                    }
                },
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "second",
                    SubjectKind = "person",
                    SubjectName = "Carrie-Anne Moss",
                    SubjectExternalIds = secondExternalId is null
                        ? new Dictionary<string, string>()
                        : new Dictionary<string, string> { ["catalog"] = secondExternalId },
                    InitialSubjectContribution = new MetadataContribution
                    {
                        ProviderType = "provider-b",
                        Title = " carrie-anne moss ",
                        OriginalTitle = " carrie-anne moss ",
                        BirthDate = secondBirthDay == 0 ? null : new DateTime(1967, 1, secondBirthDay)
                    }
                }
            ]);

        var testLogger = new Mock<ILogger<ContentMetadataRefreshItemJob>>();
        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            TimeProvider.System,
            testLogger.Object,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _subjectRepository.Verify(
                repository => repository.TryCreateAsync(
                    It.IsAny<Subject>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(expectedSubjectCount));
            testLogger.Verify(
                value => value.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(
                        "unsupported fallback identity fields",
                        StringComparison.Ordinal)),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Exactly(expectedWarningCount));
        }
    }

    [Test]
    public async Task ExecuteAsync_CreatesSubject_WhenContainerHasConflictingExternalId()
    {
        // Arrange
        _subjectKindRegistry.SetupGet(registry => registry.Definitions).Returns(
        [
            new SubjectKindDefinition
            {
                Slug = "person",
                CustomMetadataFields = [],
                FallbackIdentityFields = MetadataField.Title | MetadataField.BirthDate
            }
        ]);
        var provider = new Mock<IContentHierarchyMetadataRelationshipProvider>();
        provider.Setup(value => value.ProviderType).Returns(ProviderType);
        provider.Setup(value => value.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(value => value.TryGetMetadataAsync(
                It.IsAny<MetadataFetchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" }
            });
        provider
            .Setup(value => value.GetRelationshipsAsync(
                It.IsAny<MetadataRelationshipRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "a",
                    SubjectKind = "person",
                    SubjectName = "Jane",
                    SubjectExternalIds = new Dictionary<string, string> { ["provider-b"] = "1" },
                    InitialSubjectContribution = new MetadataContribution
                    {
                        ProviderType = ProviderType,
                        Title = "Jane",
                        BirthDate = new DateTime(1970, 1, 1)
                    }
                },
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "b",
                    SubjectKind = "person",
                    SubjectName = "Jane",
                    InitialSubjectContribution = new MetadataContribution
                    {
                        ProviderType = "provider-b",
                        Title = "Jane",
                        BirthDate = new DateTime(1970, 1, 1)
                    }
                },
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "c",
                    SubjectKind = "person",
                    SubjectName = "Jane",
                    SubjectExternalIds = new Dictionary<string, string> { ["provider-b"] = "2" },
                    InitialSubjectContribution = new MetadataContribution
                    {
                        ProviderType = "provider-c",
                        Title = "Jane",
                        BirthDate = new DateTime(1970, 1, 1)
                    }
                }
            ]);

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _subjectRepository.Verify(
            repository => repository.TryCreateAsync(
                It.IsAny<Subject>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task ExecuteAsync_LeavesAssociationUnresolved_WhenFallbackIdentityIsAmbiguous()
    {
        // Arrange
        _subjectKindRegistry.SetupGet(registry => registry.Definitions).Returns(
        [
            new SubjectKindDefinition
            {
                Slug = "person",
                CustomMetadataFields = [],
                FallbackIdentityFields = MetadataField.Title | MetadataField.BirthDate
            }
        ]);
        var provider = new Mock<IContentHierarchyMetadataRelationshipProvider>();
        provider.Setup(value => value.ProviderType).Returns(ProviderType);
        provider.Setup(value => value.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(value => value.TryGetMetadataAsync(
                It.IsAny<MetadataFetchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" }
            });
        provider
            .Setup(value => value.GetRelationshipsAsync(
                It.IsAny<MetadataRelationshipRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "a",
                    SubjectKind = "person",
                    SubjectName = "Jane",
                    SubjectExternalIds = new Dictionary<string, string> { ["provider-b"] = "1" },
                    InitialSubjectContribution = new MetadataContribution
                    {
                        ProviderType = ProviderType,
                        Title = "Jane",
                        BirthDate = new DateTime(1970, 1, 1)
                    }
                },
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "b",
                    SubjectKind = "person",
                    SubjectName = "Jane",
                    SubjectExternalIds = new Dictionary<string, string> { ["provider-b"] = "2" },
                    InitialSubjectContribution = new MetadataContribution
                    {
                        ProviderType = "provider-b",
                        Title = "Jane",
                        BirthDate = new DateTime(1970, 1, 1)
                    }
                },
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "c",
                    SubjectKind = "person",
                    SubjectName = "Jane",
                    SubjectExternalIds = new Dictionary<string, string> { ["imdb"] = "3" },
                    InitialSubjectContribution = new MetadataContribution
                    {
                        ProviderType = "provider-c",
                        Title = "Jane",
                        BirthDate = new DateTime(1970, 1, 1)
                    }
                }
            ]);
        var testLogger = new Mock<ILogger<ContentMetadataRefreshItemJob>>();

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            TimeProvider.System,
            testLogger.Object,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _subjectRepository.Verify(
                repository => repository.TryCreateAsync(
                    It.IsAny<Subject>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            _containerRepository.Verify(repository => repository.AddRelationshipAsync(
                It.Is<Relationship>(relationship => relationship.Qualifier == "c"),
                It.IsAny<CancellationToken>()), Times.Never);
            testLogger.Verify(
                value => value.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) =>
                        state.ToString()!.Contains("ambiguous", StringComparison.Ordinal)),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_UsesPersistedSubject_WhenInRunSubjectMatchesOnlyByFallbackIdentity()
    {
        // Arrange
        var persistedContainerId = Guid.NewGuid();
        var persistedSubjectId = Guid.NewGuid();
        _subjectRepository.Setup(repository => repository.ListIdentityDetailsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(persistedSubjectId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Subject
            {
                Id = persistedSubjectId,
                Kind = "person",
                MetadataContainerId = persistedContainerId,
                MetadataContainer = new MetadataContainer
                {
                    Id = persistedContainerId,
                    Contributions =
                    {
                        new MetadataContribution
                        {
                            ProviderType = "imdb",
                            ExternalIds = { new ExternalId { Source = "imdb", Value = "nm9" } }
                        }
                    }
                }
            }]);
        _subjectKindRegistry.SetupGet(registry => registry.Definitions).Returns(
        [
            new SubjectKindDefinition
            {
                Slug = "person",
                CustomMetadataFields = [],
                FallbackIdentityFields = MetadataField.Title | MetadataField.BirthDate
            }
        ]);
        _subjectRepository
            .Setup(repository => repository.ListIdsByExternalIdentityAsync("person",
                It.Is<IReadOnlyCollection<ExternalId>>(ids => ids.Any(id => id.Source == "imdb" && id.Value == "nm9")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([persistedSubjectId]);
        var provider = new Mock<IContentHierarchyMetadataRelationshipProvider>();
        provider.Setup(value => value.ProviderType).Returns(ProviderType);
        provider.Setup(value => value.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(value => value.TryGetMetadataAsync(
                It.IsAny<MetadataFetchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" }
            });
        provider
            .Setup(value => value.GetRelationshipsAsync(
                It.IsAny<MetadataRelationshipRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "catalog-credit",
                    SubjectKind = "person",
                    SubjectName = "Jane Doe",
                    SubjectExternalIds = new Dictionary<string, string> { ["catalog"] = "100" },
                    InitialSubjectContribution = new MetadataContribution
                    {
                        ProviderType = ProviderType,
                        Title = "Jane Doe",
                        BirthDate = new DateTime(1970, 1, 1)
                    }
                },
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "imdb-credit",
                    SubjectKind = "person",
                    SubjectName = "Jane Doe",
                    SubjectExternalIds = new Dictionary<string, string> { ["imdb"] = "nm9" },
                    InitialSubjectContribution = new MetadataContribution
                    {
                        ProviderType = "provider-b",
                        Title = "Jane Doe",
                        BirthDate = new DateTime(1970, 1, 1)
                    }
                }
            ]);

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _subjectRepository.Verify(
                repository => repository.TryCreateAsync(
                    It.IsAny<Subject>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _containerRepository.Verify(
                repository => repository.AddRelationshipAsync(
                    It.Is<Relationship>(relationship =>
                        relationship.Qualifier == "imdb-credit" &&
                        relationship.RelatedMetadataContainerId == persistedContainerId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_ReusesFallbackIdentity_WhenLaterDefinitionMatchesItsExternalId()
    {
        // Arrange
        _subjectKindRegistry.SetupGet(registry => registry.Definitions).Returns(
        [
            new SubjectKindDefinition
            {
                Slug = "person",
                CustomMetadataFields = [],
                FallbackIdentityFields = MetadataField.Title | MetadataField.BirthDate
            }
        ]);
        var provider = new Mock<IContentHierarchyMetadataRelationshipProvider>();
        provider.Setup(value => value.ProviderType).Returns(ProviderType);
        provider.Setup(value => value.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(value => value.TryGetMetadataAsync(
                It.IsAny<MetadataFetchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" }
            });
        provider
            .Setup(value => value.GetRelationshipsAsync(
                It.IsAny<MetadataRelationshipRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "catalog-credit",
                    SubjectKind = "person",
                    SubjectName = "Jane Doe",
                    SubjectExternalIds = new Dictionary<string, string> { ["catalog"] = "100" },
                    InitialSubjectContribution = new MetadataContribution
                    {
                        ProviderType = ProviderType,
                        Title = "Jane Doe",
                        BirthDate = new DateTime(1970, 1, 1)
                    }
                },
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "first-imdb-credit",
                    SubjectKind = "person",
                    SubjectName = "Jane Doe",
                    SubjectExternalIds = new Dictionary<string, string> { ["imdb"] = "nm9" },
                    InitialSubjectContribution = new MetadataContribution
                    {
                        ProviderType = "provider-b",
                        Title = "Jane Doe",
                        BirthDate = new DateTime(1970, 1, 1)
                    }
                },
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "second-imdb-credit",
                    SubjectKind = "person",
                    SubjectName = "Jane Doe",
                    SubjectExternalIds = new Dictionary<string, string> { ["imdb"] = "nm9" },
                    InitialSubjectContribution = new MetadataContribution
                    {
                        ProviderType = "provider-c",
                        Title = "Jane Doe"
                    }
                }
            ]);

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _subjectRepository.Verify(
            repository => repository.TryCreateAsync(
                It.IsAny<Subject>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_UsesExternalIdOnly_WhenKindDeclaresNoFallbackFields()
    {
        // Arrange
        var existingContainerId = Guid.NewGuid();
        var existingSubjectId = Guid.NewGuid();
        _subjectRepository.Setup(repository => repository.ListIdentityDetailsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(existingSubjectId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Subject
            {
                Id = existingSubjectId,
                Kind = "person",
                MetadataContainerId = existingContainerId,
                MetadataContainer = new MetadataContainer
                {
                    Id = existingContainerId,
                    Contributions =
                    {
                        new MetadataContribution
                        {
                            ProviderType = "catalog",
                            ExternalIds = { new ExternalId { Source = "catalog", Value = "shared" } }
                        }
                    }
                }
            }]);
        _subjectRepository
            .Setup(repository => repository.ListIdsByExternalIdentityAsync("person",
                It.Is<IReadOnlyCollection<ExternalId>>(ids => ids.Any(id =>
                    id.Source == "catalog" && id.Value == "shared")), It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingSubjectId]);
        var provider = new Mock<IContentHierarchyMetadataRelationshipProvider>();
        provider.Setup(value => value.ProviderType).Returns(ProviderType);
        provider.Setup(value => value.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(value => value.TryGetMetadataAsync(
                It.IsAny<MetadataFetchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" }
            });
        provider
            .Setup(value => value.GetRelationshipsAsync(
                It.IsAny<MetadataRelationshipRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubjectRelationshipDefinition
                {
                    RelationshipKind = "cast",
                    RelationshipQualifier = "Trinity",
                    SubjectKind = "person",
                    SubjectName = "Carrie-Anne Moss",
                    SubjectExternalIds = new Dictionary<string, string> { ["catalog"] = "shared" }
                }
            ]);

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _subjectRepository.Verify(
                repository => repository.TryCreateAsync(
                    It.IsAny<Subject>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _containerRepository.Verify(
                repository => repository.AddRelationshipAsync(
                    It.Is<Relationship>(relationship =>
                        relationship.RelatedMetadataContainerId == existingContainerId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_MaterializesChildren_WhenProviderDeclaresHierarchy()
    {
        // Arrange
        var provider = new Mock<IContentHierarchyMetadataProvider>();

        provider.Setup(p => p.ProviderType).Returns(ProviderType);
        provider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" },
                Children =
                [
                    new ChildHierarchyDefinition
                    {
                        Level = "season",
                        Name = "Season One",
                        Slug = "season-1",
                        Ordinal = 1,
                        AlignmentKey = "season:1",
                        ExternalIds = new Dictionary<string, string> { [ProviderType] = "season-1" },
                        InitialContribution = new MetadataContribution
                        {
                            ProviderType = ProviderType,
                            Title = "Season One"
                        },
                        Children =
                        [
                            new ChildHierarchyDefinition
                            {
                                Level = "episode",
                                Name = "Episode One",
                                Slug = "s01e01",
                                Ordinal = 1,
                                AlignmentKey = "episode:1:1",
                                ExternalIds = new Dictionary<string, string> { [ProviderType] = "episode-1" },
                                InitialContribution = new MetadataContribution
                                {
                                    ProviderType = ProviderType,
                                    Title = "Episode One"
                                }
                            }
                        ]
                    }
                ]
            });
        var createdChildIds = new Dictionary<string, Guid>();
        _contentRepository
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<ContentHierarchyItem, Guid?, CancellationToken>(
                (child, _, _) => createdChildIds[child.Level] = child.Id)
            .ReturnsAsync(true);

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _contentRepository.Verify(
                repository => repository.TryCreateAsync(
                    It.Is<ContentHierarchyItem>(child =>
                        child.ParentId == _itemId &&
                        child.ContentType == "movie" &&
                        child.Level == "season" &&
                        child.Name == "Season One" &&
                        child.Slug == "season-1" &&
                        child.Ordinal == 1 &&
                        child.AlignmentKey == "season:1" &&
                        child.Path == "Season One" &&
                        child.Settings["content.path.origin"] == "Generated" &&
                        child.PendingMetadataRefresh),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentRepository.Verify(
                repository => repository.TryCreateAsync(
                    It.Is<ContentHierarchyItem>(child =>
                        child.Level == "episode" &&
                        child.ParentId == createdChildIds["season"] &&
                        child.Name == "Episode One" &&
                        child.Slug == "s01e01" &&
                        child.Ordinal == 1 &&
                        child.AlignmentKey == "episode:1:1" &&
                        child.PendingMetadataRefresh),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _containerRepository.Verify(
                repository => repository.AddContributionAsync(
                    It.IsAny<Guid>(),
                    It.Is<MetadataContribution>(contribution =>
                        contribution.Title == "Season One" &&
                        contribution.ExternalIds.Any(externalId =>
                            externalId.Source == ProviderType && externalId.Value == "season-1")),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _containerRepository.Verify(
                repository => repository.AddContributionAsync(
                    It.IsAny<Guid>(),
                    It.Is<MetadataContribution>(contribution =>
                        contribution.Title == "Episode One" &&
                        contribution.ExternalIds.Any(externalId =>
                            externalId.Source == ProviderType && externalId.Value == "episode-1")),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _jobManager.Verify(
                manager => manager.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(request =>
                        request.JobType == "ContentMetadataRefreshItem" &&
                        request.Parameters != null &&
                        request.Parameters["contentHierarchyItemId"] ==
                            createdChildIds["episode"].ToString("D")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _jobManager.Verify(
                manager => manager.ExecuteAsync(
                    It.IsAny<JobEnqueueRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentRepository.Verify(
                repository => repository.QueryAsync(
                    It.IsAny<ContentHierarchyItemCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotDuplicateChildren_WhenSameHierarchyIsDeclaredTwice()
    {
        // Arrange
        var existingChildId = Guid.NewGuid();
        var existingContainerId = Guid.NewGuid();
        var existingChild = new ContentHierarchyItem
        {
            Id = existingChildId,
            MetadataContainerId = existingContainerId,
            MetadataContainer = new MetadataContainer { Id = existingContainerId },
            ParentId = _itemId,
            ContentType = "movie",
            Level = "season",
            Name = "Season One",
            Slug = "season-1",
            AlignmentKey = "season:1"
        };
        var existingContribution = new MetadataContribution { ProviderType = ProviderType };
        existingContribution.ExternalIds.Add(new ExternalId
        {
            Source = ProviderType,
            Value = "season-1"
        });
        existingChild.MetadataContainer!.Contributions.Add(existingContribution);

        var provider = new Mock<IContentHierarchyMetadataProvider>();
        provider.Setup(p => p.ProviderType).Returns(ProviderType);
        provider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" },
                Children =
                [
                    new ChildHierarchyDefinition
                    {
                        Level = "season",
                        Name = "Season One",
                        Slug = "season-1",
                        AlignmentKey = "season:1",
                        ExternalIds = new Dictionary<string, string> { [ProviderType] = "season-1" }
                    }
                ]
            });
        _contentRepository
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentRepository
            .SetupSequence(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>())
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [existingChild] });

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _contentRepository.Verify(
                repository => repository.TryCreateAsync(
                    It.IsAny<ContentHierarchyItem>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _containerRepository.Verify(
                repository => repository.AddContributionAsync(
                    It.IsNotIn(_containerId),
                    It.IsAny<MetadataContribution>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _jobManager.Verify(
                manager => manager.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(request => request.JobType == "ContentMetadataRefreshItem"),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotDuplicateChild_WhenLevelAndSlugMatchWithoutAlignmentKey()
    {
        // Arrange
        var existingChildId = Guid.NewGuid();
        var existingContainerId = Guid.NewGuid();
        var existingChild = new ContentHierarchyItem
        {
            Id = existingChildId,
            MetadataContainerId = existingContainerId,
            MetadataContainer = new MetadataContainer { Id = existingContainerId },
            ParentId = _itemId,
            ContentType = "movie",
            Level = "season",
            Name = "Existing Season",
            Slug = "season-1"
        };
        var provider = new Mock<IContentHierarchyMetadataProvider>();
        provider.Setup(p => p.ProviderType).Returns(ProviderType);
        provider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" },
                Children =
                [
                    new ChildHierarchyDefinition
                    {
                        Level = "season",
                        Name = "Season One",
                        Slug = "season-1"
                    }
                ]
            });
        _contentRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [existingChild] });

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _contentRepository.Verify(
                repository => repository.TryCreateAsync(
                    It.IsAny<ContentHierarchyItem>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _jobManager.Verify(
                manager => manager.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(request =>
                        request.Parameters != null &&
                        request.Parameters[
                            "contentHierarchyItemId"] == existingChildId.ToString("D")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotDuplicateChild_WhenSecondProviderMatchesByExternalIdOnly()
    {
        // Arrange
        const string secondProviderType = "provider-b";

        var firstProvider = new Mock<IContentHierarchyMetadataProvider>();

        firstProvider.Setup(p => p.ProviderType).Returns(ProviderType);
        firstProvider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        firstProvider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" },
                Children =
                [
                    new ChildHierarchyDefinition
                    {
                        Level = "season",
                        Name = "Season One",
                        Slug = "season-1",
                        ExternalIds = new Dictionary<string, string> { ["catalog"] = "shared-season" }
                    }
                ]
            });
        var secondProvider = new Mock<IContentHierarchyMetadataProvider>();

        secondProvider.Setup(p => p.ProviderType).Returns(secondProviderType);
        secondProvider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        secondProvider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = secondProviderType, Title = "The Matrix" },
                Children =
                [
                    new ChildHierarchyDefinition
                    {
                        Level = "season",
                        Name = "First Season",
                        Slug = "first-season",
                        ExternalIds = new Dictionary<string, string>
                        {
                            ["catalog"] = "shared-season",
                            [secondProviderType] = "season-b"
                        }
                    }
                ]
            });
        _contentRepository
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var subject = new ContentMetadataRefreshItemJob(
            [firstProvider.Object, secondProvider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _contentRepository.Verify(
                repository => repository.TryCreateAsync(
                    It.IsAny<ContentHierarchyItem>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _containerRepository.Verify(
                repository => repository.AddContributionAsync(
                    It.IsNotIn(_containerId),
                    It.Is<MetadataContribution>(contribution =>
                        contribution.ProviderType == secondProviderType &&
                        contribution.ExternalIds.Any(externalId =>
                            externalId.Source == secondProviderType && externalId.Value == "season-b")),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _jobManager.Verify(
                manager => manager.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(request => request.JobType == "ContentMetadataRefreshItem"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentRepository.Verify(
                repository => repository.QueryAsync(
                    It.IsAny<ContentHierarchyItemCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsChild_WhenProviderDeclaresInvalidLevel()
    {
        // Arrange
        var provider = new Mock<IContentHierarchyMetadataProvider>();
        provider.Setup(p => p.ProviderType).Returns(ProviderType);
        provider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" },
                Children =
                [
                    new ChildHierarchyDefinition
                    {
                        Level = "episode",
                        Name = "Episode One",
                        Slug = "s01e01"
                    }
                ]
            });

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _contentRepository.Verify(
            repository => repository.TryCreateAsync(
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void ExecuteAsync_ThrowsMetadataNotReturned_WhenProviderFailsAndAnotherDeclaresChildren()
    {
        // Arrange
        _contentRepository
            .Setup(repository => repository.GetByIdAsync(_itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = _itemId,
                MetadataContainerId = _containerId,
                MetadataContainer = new MetadataContainer { Id = _containerId },
                ContentType = "movie",
                Level = "movie",
                Slug = "the-matrix",
                Name = "The Matrix",
                PendingMetadataRefresh = true
            });

        var failingProvider = new Mock<IContentHierarchyMetadataProvider>();
        failingProvider.Setup(p => p.ProviderType).Returns(ProviderType);
        failingProvider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        failingProvider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient failure"));

        var childProvider = new Mock<IContentHierarchyMetadataProvider>();
        childProvider.Setup(p => p.ProviderType).Returns("provider-b");
        childProvider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        childProvider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Children =
                [
                    new ChildHierarchyDefinition
                    {
                        Level = "season",
                        Name = "Season One",
                        Slug = "season-1"
                    }
                ]
            });

        var subject = new ContentMetadataRefreshItemJob(
            [failingProvider.Object, childProvider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        var exception = Assert.ThrowsAsync<MetadataNotReturnedException>(() =>
            subject.ExecuteAsync(_jobContext));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("A metadata provider failed."));
    }

    [Test]
    public async Task ExecuteAsync_MarksRefreshed_WhenAProviderFailsAndAnotherDeclaresChildrenForATitledItem()
    {
        // Arrange
        var container = new MetadataContainer { Id = _containerId };
        container.Contributions.Add(new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" });
        _contentRepository
            .Setup(repository => repository.GetByIdAsync(_itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = _itemId,
                MetadataContainerId = _containerId,
                MetadataContainer = container,
                ContentType = "movie",
                Level = "movie",
                Slug = "the-matrix",
                Name = "The Matrix"
            });
        _contentRepository
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var failingProvider = new Mock<IContentHierarchyMetadataProvider>();
        failingProvider.Setup(p => p.ProviderType).Returns(ProviderType);
        failingProvider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        failingProvider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient failure"));

        var childProvider = new Mock<IContentHierarchyMetadataProvider>();
        childProvider.Setup(p => p.ProviderType).Returns("provider-b");
        childProvider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        childProvider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Children =
                [
                    new ChildHierarchyDefinition
                    {
                        Level = "season",
                        Name = "Season One",
                        Slug = "season-1"
                    }
                ]
            });

        var subject = new ContentMetadataRefreshItemJob(
            [failingProvider.Object, childProvider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _contentRepository.Verify(
            repository => repository.PatchAsync(
                It.Is<ContentHierarchyItemPatch>(patch =>
                    !patch.PendingMetadataRefresh!.Value &&
                    patch.DateLastRefreshed!.Value == new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_StampsTheAttemptAndKeepsTheItemPending_WhenNoProviderReturnsMetadata()
    {
        const string supportedContentType = "movie";

        // Arrange
        _contentRepository
            .Setup(repository => repository.GetByIdAsync(_itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = _itemId,
                MetadataContainerId = _containerId,
                MetadataContainer = new MetadataContainer { Id = _containerId },
                ContentType = "movie",
                Level = "movie",
                Slug = "the-matrix",
                Name = "the-matrix",
                PendingMetadataRefresh = true
            });

        var provider = new Mock<IContentHierarchyMetadataProvider>();
        provider.Setup(p => p.ProviderType).Returns(ProviderType);
        provider.Setup(p => p.SupportedContentTypes).Returns([supportedContentType]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentHierarchyMetadataResult?)null);

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _contentRepository.Verify(
                repository => repository.PatchAsync(
                    It.Is<ContentHierarchyItemPatch>(patch =>
                        patch.Id == _itemId &&
                        patch.DateLastRefreshed!.Value == new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc) &&
                        patch.PendingMetadataRefresh == null),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentRepository.Verify(
                repository => repository.UpdateAsync(
                    It.IsAny<ContentHierarchyItem>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _eventBus.Verify(
                bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase("book", JobExecutionMode.Queue)]
    [TestCase("movie", JobExecutionMode.Immediate)]
    public async Task ExecuteAsync_DoesNotStampTheAttempt_WhenNoProviderWasAskedOrTheOperatorAsked(
        string supportedContentType,
        JobExecutionMode executionMode)
    {
        // Arrange
        _contentRepository
            .Setup(repository => repository.GetByIdAsync(_itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = _itemId,
                MetadataContainerId = _containerId,
                MetadataContainer = new MetadataContainer { Id = _containerId },
                ContentType = "movie",
                Level = "movie",
                Slug = "the-matrix",
                Name = "the-matrix",
                PendingMetadataRefresh = true
            });

        var provider = new Mock<IContentHierarchyMetadataProvider>();
        provider.Setup(p => p.ProviderType).Returns(ProviderType);
        provider.Setup(p => p.SupportedContentTypes).Returns([supportedContentType]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentHierarchyMetadataResult?)null);

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: executionMode,
            Parameters: new Dictionary<string, string> { ["contentHierarchyItemId"] = _itemId.ToString("D") },
            Event: null);

        // Act
        await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _contentRepository.Verify(
                repository => repository.PatchAsync(
                    It.IsAny<ContentHierarchyItemPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _contentRepository.Verify(
                repository => repository.UpdateAsync(
                    It.IsAny<ContentHierarchyItem>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesExistingChildren_WhenProviderDeclaresNoChildren()
    {
        // Arrange
        var existingChildId = Guid.NewGuid();
        var existingContainerId = Guid.NewGuid();
        var existingChild = new ContentHierarchyItem
        {
            Id = existingChildId,
            MetadataContainerId = existingContainerId,
            MetadataContainer = new MetadataContainer { Id = existingContainerId },
            ContentType = "movie",
            Level = "season",
            Name = "Season One",
            Slug = "season-1"
        };

        var existingContribution = new MetadataContribution { ProviderType = ProviderType };
        existingContribution.ExternalIds.Add(new ExternalId { Source = ProviderType, Value = "season-1" });
        existingChild.MetadataContainer!.Contributions.Add(existingContribution);

        var provider = new Mock<IContentHierarchyMetadataProvider>();
        provider.Setup(p => p.ProviderType).Returns(ProviderType);
        provider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" }
            });
        _contentRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [existingChild]
            });

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(
                manager => manager.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(request =>
                        request.JobType == "ContentMetadataRefreshItem" &&
                        request.Parameters != null &&
                        request.Parameters[
                            "contentHierarchyItemId"] == existingChildId.ToString("D")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentRepository.Verify(
                repository => repository.QueryAsync(
                    It.IsAny<ContentHierarchyItemCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public void ExecuteAsync_StampsTheAttemptBeforeItThrows_WhenAProviderFails()
    {
        // Arrange
        _contentRepository
            .Setup(repository => repository.GetByIdAsync(_itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = _itemId,
                MetadataContainerId = _containerId,
                MetadataContainer = new MetadataContainer { Id = _containerId },
                ContentType = "movie",
                Level = "movie",
                Slug = "the-matrix",
                Name = "the-matrix",
                PendingMetadataRefresh = true
            });

        var provider = new Mock<IContentHierarchyMetadataProvider>();
        provider.Setup(p => p.ProviderType).Returns(ProviderType);
        provider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("502 Bad Gateway"));

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        var act = () => subject.ExecuteAsync(_jobContext);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(act, Throws.TypeOf<MetadataNotReturnedException>());
            _contentRepository.Verify(
                repository => repository.PatchAsync(
                    It.Is<ContentHierarchyItemPatch>(patch =>
                        patch.Id == _itemId &&
                        patch.DateLastRefreshed!.Value == new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public void ExecuteAsync_ThrowsMetadataNotReturned_WhenEveryProviderFailsForATitledItem(
        bool pendingMetadataRefresh,
        int expectedStamps)
    {
        // Arrange
        var container = new MetadataContainer { Id = _containerId };
        container.Contributions.Add(new MetadataContribution
        {
            ProviderType = ProviderType,
            Title = "The Matrix",
            DateLastRefreshed = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        _contentRepository
            .Setup(repository => repository.GetByIdAsync(_itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = _itemId,
                MetadataContainerId = _containerId,
                MetadataContainer = container,
                ContentType = "movie",
                Level = "movie",
                Slug = "the-matrix",
                Name = "The Matrix",
                PendingMetadataRefresh = pendingMetadataRefresh
            });

        var provider = new Mock<IContentHierarchyMetadataProvider>();
        provider.Setup(p => p.ProviderType).Returns(ProviderType);
        provider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The provider could not read the stored identifier."));

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        var exception = Assert.ThrowsAsync<MetadataNotReturnedException>(() =>
            subject.ExecuteAsync(_jobContext));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("A metadata provider failed."));
            _transactionManager.Verify(
                manager => manager.ExecuteWithTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _contentRepository.Verify(
                repository => repository.PatchAsync(
                    It.Is<ContentHierarchyItemPatch>(patch => patch.PendingMetadataRefresh != null),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _contentRepository.Verify(
                repository => repository.PatchAsync(
                    It.Is<ContentHierarchyItemPatch>(patch => patch.DateLastRefreshed != null),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(expectedStamps));
        }
    }

    [Test]
    public void ExecuteAsync_KeepsTheMonitoringEdit_WhenAProviderFailsAfterTheUserUnmonitorsTheItem()
    {
        // Arrange
        _contentRepository
            .Setup(repository => repository.GetByIdAsync(_itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = _itemId,
                MetadataContainerId = _containerId,
                MetadataContainer = new MetadataContainer { Id = _containerId },
                ContentType = "movie",
                Level = "movie",
                Slug = "the-matrix",
                Name = "the-matrix",
                Monitoring = MonitoringMode.Monitored,
                PendingMetadataRefresh = true
            });

        var provider = new Mock<IContentHierarchyMetadataProvider>();
        provider.Setup(p => p.ProviderType).Returns(ProviderType);
        provider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("502 Bad Gateway"));

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        var act = () => subject.ExecuteAsync(_jobContext);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(act, Throws.TypeOf<MetadataNotReturnedException>());
            _contentRepository.Verify(
                repository => repository.PatchAsync(
                    It.Is<ContentHierarchyItemPatch>(patch => patch.Monitoring != null),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsTheMonitoringEdit_WhenTheUserUnmonitorsTheItemDuringTheFetch()
    {
        // Arrange
        _contentRepository
            .SetupSequence(repository => repository.GetByIdAsync(_itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = _itemId,
                MetadataContainerId = _containerId,
                MetadataContainer = new MetadataContainer { Id = _containerId },
                ContentType = "movie",
                Level = "movie",
                Slug = "the-matrix",
                Name = "the-matrix",
                Monitoring = MonitoringMode.Monitored,
                PendingMetadataRefresh = true
            })
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = _itemId,
                MetadataContainerId = _containerId,
                MetadataContainer = new MetadataContainer { Id = _containerId },
                ContentType = "movie",
                Level = "movie",
                Slug = "the-matrix",
                Name = "the-matrix",
                Monitoring = MonitoringMode.Unmonitored,
                PendingMetadataRefresh = true
            });

        var provider = new Mock<IContentHierarchyMetadataProvider>();
        provider.Setup(p => p.ProviderType).Returns(ProviderType);
        provider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = ProviderType, Title = "The Matrix" }
            });

        var subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            _subjectKindRegistry.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);

        // Act
        await subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _contentRepository.Verify(
            repository => repository.PatchAsync(
                It.Is<ContentHierarchyItemPatch>(patch =>
                    patch.Monitoring == null &&
                    patch.Name!.Value == "The Matrix" &&
                    !patch.PendingMetadataRefresh!.Value &&
                    patch.DateLastRefreshed!.Value == new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCaseSource(nameof(RefreshFailures))]
    public void ExecuteAsync_QueuesOneResumeAndRethrows_WhenTheRefreshThrowsWhileAnImportWaits(
        Exception failure,
        CancellationToken cancellationToken)
    {
        // Arrange
        _contentRepository
            .Setup(repository => repository.GetByIdAsync(_itemId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        var candidates = new Mock<ILibraryImportCandidateRepository>();
        candidates
            .Setup(repository => repository.QueryAsync(
                It.Is<LibraryImportCandidateQuery>(query =>
                    query.Status == LibraryImportCandidateStatus.Importing &&
                    query.ContentHierarchyItemIds!.Contains(_itemId)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate>
            {
                Items =
                [
                    new LibraryImportCandidate
                    {
                        RootFolderId = Guid.Empty,
                        ScanRunId = Guid.Empty,
                        FilePath = "/media/movies/The Matrix/The.Matrix.mkv",
                        ContentType = "movie",
                        Status = LibraryImportCandidateStatus.Importing
                    }
                ]
            });
        var subject = new ContentMetadataRefreshItemJob(
            [],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            candidates.Object,
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);
        var context = new JobContext(
            Guid.NewGuid(), null, null, null, JobExecutionMode.Queue,
            new Dictionary<string, string> { ["contentHierarchyItemId"] = _itemId.ToString("D") }, null);

        // Act
        var thrown = Assert.CatchAsync(() => subject.ExecuteAsync(context, cancellationToken));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thrown, Is.SameAs(failure));
            candidates.Verify(
                repository => repository.QueryAsync(
                    It.IsAny<LibraryImportCandidateQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    CancellationToken.None),
                Times.Once);
            _jobManager.Verify(
                manager => manager.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(request =>
                        request.JobType == "RootFolderContentImportResume" &&
                        request.Parameters!["contentHierarchyItemId"] == _itemId.ToString("D")),
                    CancellationToken.None),
                Times.Once);
        }
    }

    [TestCaseSource(nameof(WaitingImports))]
    public async Task ExecuteAsync_QueuesAResumeOnlyForAWaitingImport_WhenTheRefreshCompletes(
        LibraryImportCandidate[] waiting,
        int expectedResumes)
    {
        // Arrange
        var candidates = new Mock<ILibraryImportCandidateRepository>();
        candidates
            .Setup(repository => repository.QueryAsync(
                It.Is<LibraryImportCandidateQuery>(query =>
                    query.Status == LibraryImportCandidateStatus.Importing &&
                    query.ContentHierarchyItemIds!.Contains(_itemId)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate> { Items = waiting });
        var subject = new ContentMetadataRefreshItemJob(
            [],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            candidates.Object,
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);
        var context = new JobContext(
            Guid.NewGuid(), null, null, null, JobExecutionMode.Queue,
            new Dictionary<string, string> { ["contentHierarchyItemId"] = _itemId.ToString("D") }, null);

        // Act
        await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "RootFolderContentImportResume" &&
                    request.Parameters!["contentHierarchyItemId"] == _itemId.ToString("D")),
                CancellationToken.None),
            Times.Exactly(expectedResumes));
    }

    [Test]
    public void ExecuteAsync_CompletesTheRefresh_WhenTheHandOverFails()
    {
        // Arrange
        var candidates = new Mock<ILibraryImportCandidateRepository>();
        candidates
            .Setup(repository => repository.QueryAsync(
                It.IsAny<LibraryImportCandidateQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The store is unavailable."));
        var subject = new ContentMetadataRefreshItemJob(
            [],
            _contentRepository.Object,
            _containerRepository.Object,
            _subjectRepository.Object,
            _eventBus.Object,
            _contentTypeRegistry.Object,
            _transactionManager.Object,
            _jobManager.Object,
            candidates.Object,
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            _progressReporter);
        var context = new JobContext(
            Guid.NewGuid(), null, null, null, JobExecutionMode.Queue,
            new Dictionary<string, string> { ["contentHierarchyItemId"] = _itemId.ToString("D") }, null);

        // Act
        Assert.DoesNotThrowAsync(() => subject.ExecuteAsync(context));

        // Assert
        _jobManager.Verify(
            manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
