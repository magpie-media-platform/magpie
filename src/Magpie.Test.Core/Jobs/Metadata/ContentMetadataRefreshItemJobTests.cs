using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Metadata;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Metadata;

[TestFixture]
public sealed class ContentMetadataRefreshItemJobTests
{
    private const string ProviderType = "provider-a";

    private Guid _itemId;
    private Guid _containerId;
    private SubjectRelationshipDefinition _credit = null!;
    private Mock<IContentHierarchyItemRepository> _contentRepository = null!;
    private Mock<IMetadataContainerRepository> _containerRepository = null!;
    private Mock<ISubjectRepository> _subjectRepository = null!;
    private Mock<IContentTypeRegistry> _contentTypeRegistry = null!;
    private Mock<ISubjectKindRegistry> _subjectKindRegistry = null!;
    private Mock<ITransactionManager> _transactionManager = null!;
    private Mock<IJobManager> _jobManager = null!;
    private Mock<IEventBus> _eventBus = null!;

    [SetUp]
    public void SetUp()
    {
        _itemId = Guid.NewGuid();
        _containerId = Guid.NewGuid();
        _credit = new SubjectRelationshipDefinition
        {
            RelationshipKind = "cast",
            RelationshipQualifier = "Trinity",
            SubjectKind = "person",
            SubjectName = "Carrie-Anne Moss"
        };

        _contentRepository = new Mock<IContentHierarchyItemRepository>();
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

        _containerRepository = new Mock<IMetadataContainerRepository>();
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

        _subjectRepository = new Mock<ISubjectRepository>();
        _subjectRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<Subject>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Subject subject, Guid? _, CancellationToken _) => new Subject
            {
                Id = Guid.NewGuid(),
                Kind = subject.Kind,
                MetadataContainerId = Guid.NewGuid()
            });

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

        _subjectKindRegistry = new Mock<ISubjectKindRegistry>();
        _subjectKindRegistry.Setup(registry => registry.Contains("person")).Returns(true);

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
    public async Task RefreshAsync_LinksTheCreditOnce_WhenTheProviderNamesTheRelationship()
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
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance);

        // Act
        await subject.RefreshAsync(_itemId, null, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            provider.Verify(
                p => p.GetRelationshipsAsync(
                    It.IsAny<MetadataRelationshipRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _subjectRepository.Verify(
                repository => repository.CreateAsync(
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
    public async Task RefreshAsync_LinksBothCredits_WhenTwoPeopleWithoutExternalIdsShareEveryCreditField()
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
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance);

        // Act
        await subject.RefreshAsync(_itemId, null, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _subjectRepository.Verify(
                repository => repository.CreateAsync(
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

    [Test]
    public async Task RefreshAsync_MaterializesChildren_WhenProviderDeclaresHierarchy()
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
        _contentRepository
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
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
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance);

        // Act
        await subject.RefreshAsync(_itemId, null, null, null).ConfigureAwait(false);

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
                    It.Is<ContentHierarchyItem>(child => child.Level == "episode"),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
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
            _jobManager.Verify(
                manager => manager.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(request => request.JobType == ContentMetadataRefreshItemJob.Spec.JobType),
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
    public async Task RefreshAsync_DoesNotDuplicateChildren_WhenSameHierarchyIsDeclaredTwice()
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
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance);

        // Act
        await subject.RefreshAsync(_itemId, null, null, null).ConfigureAwait(false);
        await subject.RefreshAsync(_itemId, null, null, null).ConfigureAwait(false);

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
                    It.Is<JobEnqueueRequest>(request => request.JobType == ContentMetadataRefreshItemJob.Spec.JobType),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
    }

    [Test]
    public async Task RefreshAsync_DoesNotDuplicateChild_WhenLevelAndSlugMatchWithoutAlignmentKey()
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
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance);

        // Act
        await subject.RefreshAsync(_itemId, null, null, null).ConfigureAwait(false);

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
                            MagpieJobs.Parameters.ContentHierarchyItemId] == existingChildId.ToString("D")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task RefreshAsync_DoesNotDuplicateChild_WhenSecondProviderMatchesByExternalIdOnly()
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
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance);

        // Act
        await subject.RefreshAsync(_itemId, null, null, null).ConfigureAwait(false);

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
                    It.Is<JobEnqueueRequest>(request => request.JobType == ContentMetadataRefreshItemJob.Spec.JobType),
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
    public async Task RefreshAsync_SkipsChild_WhenProviderDeclaresInvalidLevel()
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
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance);

        // Act
        await subject.RefreshAsync(_itemId, null, null, null).ConfigureAwait(false);

        // Assert
        _contentRepository.Verify(
            repository => repository.TryCreateAsync(
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void RefreshAsync_ThrowsMetadataNotReturned_WhenProviderFailsAndAnotherDeclaresChildren()
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
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance);

        // Act
        var exception = Assert.ThrowsAsync<MetadataNotReturnedException>(() =>
            subject.RefreshAsync(_itemId, null, null, null));

        // Assert
        Assert.That(exception, Is.Not.Null);
    }

    [Test]
    public async Task RefreshAsync_EnqueuesExistingChildren_WhenProviderDeclaresNoChildren()
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
            _subjectKindRegistry.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance);

        // Act
        await subject.RefreshAsync(_itemId, null, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(
                manager => manager.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(request =>
                        request.JobType == ContentMetadataRefreshItemJob.Spec.JobType &&
                        request.Parameters != null &&
                        request.Parameters[
                            MagpieJobs.Parameters.ContentHierarchyItemId] == existingChildId.ToString("D")),
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
}
