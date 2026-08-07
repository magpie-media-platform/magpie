using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs.Metadata;
using Magpie.Models;
using Magpie.Models.Data;
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
                _containerId,
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
                LevelDefinitions = []
            });

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
}
