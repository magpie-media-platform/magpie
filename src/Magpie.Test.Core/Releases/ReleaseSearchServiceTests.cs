using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Config;
using Magpie.Core.Releases;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Models.Releases;
using Magpie.Sdk.Caching;
using Magpie.Sdk.Content;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Releases;

[TestFixture]
public sealed class ReleaseSearchServiceTests
{
    private Mock<IContentRepository> _contentRepository = null!;
    private Mock<IContentService> _contentService = null!;
    private Mock<IContentHierarchyItemRepository> _contentHierarchyItems = null!;
    private Mock<IJobManager> _jobManager = null!;
    private Mock<IJobExecutionRepository> _jobExecutions = null!;
    private Mock<IReleaseRepository> _releaseRepository = null!;
    private Mock<IReleaseScopeRepository> _releaseScopeRepository = null!;
    private Mock<IDownloadRepository> _downloadRepository = null!;
    private Mock<IDownloadClientRepository> _downloadClients = null!;
    private Mock<IIndexerRepository> _indexers = null!;
    private Mock<IIndexerConnectionRepository> _indexerConnections = null!;
    private Mock<IIndexerIntegration> _indexerIntegration = null!;
    private Mock<IIndexerIntegration> _secondIndexerIntegration = null!;
    private Mock<IReleaseMatchContentTypeDefinitionProvider> _factProvider = null!;
    private Mock<IContentTypeRegistry> _contentTypeRegistry = null!;
    private Mock<IEventBus> _eventBus = null!;
    private Mock<IReleaseComparer> _firstReleaseComparer = null!;
    private Mock<IReleaseComparer> _secondReleaseComparer = null!;
    private Mock<IContentFactProvider> _contentFactProvider = null!;
    private Mock<IMaterializedMetadataRepository> _materializedMetadataRepository = null!;
    private Mock<IQualityTierListRepository> _qualityTierLists = null!;
    private Mock<IReleaseEvaluationService> _releaseEvaluationService = null!;
    private IReleaseEvaluationService _realReleaseEvaluationService = null!;
    private IReleaseSearchService _subject = null!;
    private ContentHierarchyItem _hierarchyItem = null!;
    private MetadataContribution _metadataContribution = null!;
    private Guid _contentHierarchyItemId;
    private Guid _contentItemId;
    private Guid _indexerId;
    private Guid _connectionId;
    private Guid _userId;
    private List<MagpieEvent> _publishedEvents = null!;

    [SetUp]
    public void SetUp()
    {
        _contentHierarchyItemId = Guid.NewGuid();
        _contentItemId = Guid.NewGuid();
        _indexerId = Guid.NewGuid();
        _connectionId = Guid.NewGuid();
        _userId = Guid.NewGuid();
        _publishedEvents = [];
        _downloadRepository = new Mock<IDownloadRepository> { CallBase = true };
        _downloadRepository
            .Setup(repository => repository.ListImportedByContentHierarchyItemAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _metadataContribution = new MetadataContribution
        {
            ProviderType = "test-metadata",
            Title = "Pilot",
            CreatedAt = DateTime.UtcNow,
        };
        var metadataContainer = new MetadataContainer
        {
            Id = _contentHierarchyItemId,
            CreatedAt = DateTime.UtcNow,
        };
        metadataContainer.Contributions.Add(_metadataContribution);
        _hierarchyItem = new ContentHierarchyItem
        {
            Id = _contentHierarchyItemId,
            ContentType = "tv",
            Level = "episode",
            Slug = "pilot",
            Name = "Pilot",
            EffectiveMonitoring = true,
            CreatedAt = DateTime.UtcNow,
            MetadataContainerId = _contentHierarchyItemId,
            MetadataContainer = metadataContainer,
        };
        _contentHierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(_contentHierarchyItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_hierarchyItem);
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(_contentHierarchyItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_hierarchyItem]);
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(_contentHierarchyItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_hierarchyItem]);
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        _contentRepository = new Mock<IContentRepository> { CallBase = true };
        _contentService = new Mock<IContentService>();
        _contentService
            .Setup(r => r.GetOrCreateContentItemAsync(
                _contentHierarchyItemId,
                It.IsAny<DateTime>(),
                _userId,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new ContentItem
                {
                    Id = _contentItemId,
                    ContentHierarchyItemId = _contentHierarchyItemId,
                    Fulfillment = FulfillmentStatus.Missing,
                    CreatedAt = DateTime.UtcNow,
                });
        _contentRepository
            .Setup(r => r.GetByIdAsync(_contentItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentItem { Id = _contentItemId });
        _contentRepository
            .Setup(r => r.UpdateAsync(It.IsAny<ContentItem>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _indexers = new Mock<IIndexerRepository> { CallBase = true };
        _indexers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _indexerConnections = new Mock<IIndexerConnectionRepository> { CallBase = true };
        _indexerConnections
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _indexerIntegration = new Mock<IIndexerIntegration>();
        _indexerIntegration
            .SetupGet(integration => integration.ProviderType)
            .Returns("test-indexer");
        _indexerIntegration
            .SetupGet(integration => integration.SupportedContentTypes)
            .Returns([]);
        _indexerIntegration
            .SetupGet(integration => integration.SearchFacets)
            .Returns([]);
        _indexerIntegration
            .SetupGet(integration => integration.SupportsKeywordSearch)
            .Returns(true);

        _secondIndexerIntegration = new Mock<IIndexerIntegration>();
        _secondIndexerIntegration
            .SetupGet(integration => integration.ProviderType)
            .Returns("second-test-indexer");
        _secondIndexerIntegration
            .SetupGet(integration => integration.SupportedContentTypes)
            .Returns([]);
        _secondIndexerIntegration
            .SetupGet(integration => integration.SearchFacets)
            .Returns([]);
        _secondIndexerIntegration
            .SetupGet(integration => integration.SupportsKeywordSearch)
            .Returns(true);

        _releaseRepository = new Mock<IReleaseRepository> { CallBase = true };
        _releaseRepository
            .Setup(r => r.CreateAsync(
                It.IsAny<Release>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release release, Guid? _, CancellationToken _) => release);
        _releaseRepository
            .Setup(r => r.MarkObservedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _releaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release>());
        _releaseScopeRepository = new Mock<IReleaseScopeRepository> { CallBase = true };
        _releaseScopeRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseScopeQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ReleaseScope>());
        _releaseScopeRepository
            .Setup(r => r.DeleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _factProvider = new Mock<IReleaseMatchContentTypeDefinitionProvider>();
        _factProvider
            .Setup(p => p.Definition)
            .Returns(new ContentTypeDefinition
            {
                Slug = "tv",
                DisplayName = "TV",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "show",
                        ParentSlug = null,
                        DisplayName = "Show",
                        CustomMetadataFields = [],
                        OrdinalKind = OrdinalKind.Name,
                    },
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "season",
                        ParentSlug = "show",
                        DisplayName = "Season",
                        CustomMetadataFields = [],
                        OrdinalKind = OrdinalKind.Number,
                    },
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "episode",
                        ParentSlug = "season",
                        DisplayName = "Episode",
                        CustomMetadataFields = [],
                        OrdinalKind = OrdinalKind.Number,
                    },
                ],
            });
        _factProvider
            .Setup(p => p.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(ReleaseMatchResult.Confident);
        _contentTypeRegistry = new Mock<IContentTypeRegistry>();
        _contentTypeRegistry
            .Setup(registry => registry.FindProviderFor("tv"))
            .Returns(_factProvider.Object);
        _contentTypeRegistry
            .Setup(registry => registry.GetDefinitionFor("tv"))
            .Returns(_factProvider.Object.Definition);
        var auditRecords = new Mock<IAuditRecordRepository> { CallBase = true };
        auditRecords
            .Setup(r => r.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuditRecord record, CancellationToken _) => record);
        _jobExecutions = new Mock<IJobExecutionRepository> { CallBase = true };
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>());
        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(t => t.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        transactionManager
            .Setup(t => t.OnCommittedAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> action, CancellationToken cancellationToken) =>
                action(cancellationToken));

        _eventBus = new Mock<IEventBus>();
        _eventBus
            .Setup(b => b.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) => _publishedEvents.Add(magpieEvent))
            .Returns(Task.CompletedTask);

        _jobManager = new Mock<IJobManager>();
        _jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _contentFactProvider = new Mock<IContentFactProvider>();
        _contentFactProvider
            .Setup(value => value.CanProvideFacts(It.IsAny<ContentTarget>()))
            .Returns(true);
        _contentFactProvider
            .Setup(value => value.GetFactsAsync(
                It.IsAny<ContentTarget>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _materializedMetadataRepository = new Mock<IMaterializedMetadataRepository> { CallBase = true };
        _materializedMetadataRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<MetadataCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                MetadataCriteria criteria,
                PagingParameter _,
                SortParameter? _,
                CancellationToken _) => new PagedList<MaterializedMetadata>
                {
                    Items = criteria.ContainerIds.Contains(_contentHierarchyItemId)
                        ?
                        [
                            new MaterializedMetadata
                            {
                                ContainerId = _contentHierarchyItemId,
                                Title = _metadataContribution.Title,
                                ExternalIds = _metadataContribution.ExternalIds.ToList(),
                            },
                        ]
                        : []
                });
        _qualityTierLists = new Mock<IQualityTierListRepository> { CallBase = true };
        _realReleaseEvaluationService = new ReleaseEvaluationService(
            _contentHierarchyItems.Object,
            _materializedMetadataRepository.Object,
            _releaseRepository.Object,
            _downloadRepository.Object,
            _qualityTierLists.Object,
            _contentTypeRegistry.Object,
            [_contentFactProvider.Object],
            TimeProvider.System,
            NullLogger<ReleaseEvaluationService>.Instance);
        _releaseEvaluationService = new Mock<IReleaseEvaluationService>();
        _releaseEvaluationService
            .Setup(value => value.CreateEvaluationContextAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken cancellationToken) =>
                _realReleaseEvaluationService.CreateEvaluationContextAsync(id, cancellationToken));
        _releaseEvaluationService
            .Setup(value => value.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()))
            .Returns((
                Release release,
                IReadOnlyList<ContentFact> facts,
                ReleaseEvaluationContext context) =>
                _realReleaseEvaluationService.AssessMatch(release, facts, context));
        _releaseEvaluationService
            .Setup(value => value.ResolveScopeAsync(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReleaseScopeResolution([_contentHierarchyItemId], [_contentHierarchyItemId]));

        _downloadClients = new Mock<IDownloadClientRepository> { CallBase = true };
        _firstReleaseComparer = new Mock<IReleaseComparer>();
        _firstReleaseComparer
            .Setup(comparer => comparer.CanCompare(It.IsAny<Release>()))
            .Returns(true);
        _secondReleaseComparer = new Mock<IReleaseComparer>();
        _secondReleaseComparer
            .Setup(comparer => comparer.CanCompare(It.IsAny<Release>()))
            .Returns(true);

        _subject = new ReleaseSearchService(
            _contentHierarchyItems.Object,
            _contentRepository.Object,
            _contentService.Object,
            _indexers.Object,
            _indexerConnections.Object,
            [_indexerIntegration.Object, _secondIndexerIntegration.Object],
            [],
            _releaseRepository.Object,
            _releaseScopeRepository.Object,
            new ReleaseBlockList(_downloadRepository.Object, TimeProvider.System),
            _downloadClients.Object,
            _contentTypeRegistry.Object,
            Mock.Of<IRootFolderService>(),
            auditRecords.Object,
            _jobExecutions.Object,
            _jobManager.Object,
            transactionManager.Object,
            _eventBus.Object,
            new ReleaseSearchQueryExecutor(
                _indexers.Object,
                _indexerConnections.Object,
                Mock.Of<IEventBus>(),
                [_indexerIntegration.Object, _secondIndexerIntegration.Object],
                [],
                TimeProvider.System,
                NullLogger<ReleaseSearchQueryExecutor>.Instance,
                _releaseEvaluationService.Object,
            cacheFactory: Mock.Of<ICacheFactory>(),
            releaseComparers: [],
            searchOptions: new SearchOptions()),
            _releaseEvaluationService.Object,
            [_firstReleaseComparer.Object, _secondReleaseComparer.Object],
            new JobRuntimeOptions(),
            new SearchOptions(),
            TimeProvider.System,
            NullLogger<ReleaseSearchService>.Instance);
    }

    [Test]
    public async Task SearchAsync_CreatesAndMarksContentItem_WhenNoReleasesAreFound()
    {
        // Arrange
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
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _contentFactProvider
            .Setup(provider => provider.GetFactsAsync(
                It.IsAny<ContentTarget>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                [new ContentFact { Value = "Alternate Pilot" }],
            ]);
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress.Object)
            .ConfigureAwait(false);

        // Assert
        _contentService.Verify(
            r => r.GetOrCreateContentItemAsync(
                _contentHierarchyItemId,
                It.IsAny<DateTime>(),
                _userId,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _contentRepository.Verify(
            r => r.UpdateAsync(
                It.Is<ContentItem>(content =>
                    content.Id == _contentItemId && content.DateLastSearched != default),
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.That(_publishedEvents, Has.Count.EqualTo(1));
        Assert.That(_publishedEvents[0].Context.Values, Does.Contain(_contentItemId.ToString("D")));
        using (Assert.EnterMultipleScope())
        {
            progress.Verify(value => value.Report(0, "The search will load the content item."), Times.Once);
            progress.Verify(
                value => value.Report(0, "The search will query indexer Test Connection."),
                Times.Once);
            progress.Verify(
                value => value.Report(0.5, "The search will query indexer Test Connection."),
                Times.Once);
            progress.Verify(value => value.Report(null, "The search will save the results."), Times.Once);
            progress.Verify(value => value.Report(1, "The release search completed."), Times.Once);
        }
    }

    [TestCase(ReleaseMatchResult.NoMatch, 0)]
    [TestCase(ReleaseMatchResult.Indeterminate, 0)]
    [TestCase(ReleaseMatchResult.Confident, 1)]
    [TestCase(ReleaseMatchResult.Identity, 1)]
    public async Task SearchAsync_PersistsOnlyPositiveMatches_WhenResultsHaveProducingFacts(
        ReleaseMatchResult matchResult,
        int expectedCreates)
    {
        // Arrange
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
        _factProvider
            .Setup(provider => provider.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(matchResult);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Court.Ordered.Smoothie.1080p",
                    ExternalId = "smoothie-1",
                },
            ]);

        // Act
        await _subject.SearchAsync(
            _contentHierarchyItemId,
            _userId,
            correlationId: null,
            currentExecutionId: null,
            progress: null).ConfigureAwait(false);

        // Assert
        _releaseRepository.Verify(
            repository => repository.CreateAsync(
                It.IsAny<Release>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(expectedCreates));
        _releaseEvaluationService.Verify(
            service => service.ResolveScopeAsync(
                It.IsAny<Release>(),
                It.Is<IReadOnlyList<ContentFact>>(facts => facts.Count == 1 && facts[0].Value == "Pilot"),
                _contentHierarchyItemId,
                It.IsAny<CancellationToken>()),
            Times.Exactly(expectedCreates));
    }

    [Test]
    public async Task SearchAsync_RetriesUpdate_WhenCreateIdentityCollides()
    {
        // Arrange
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
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Court.Ordered.Smoothie.1080p",
                    ExternalId = "smoothie-1",
                    IdentityKeys =
                    {
                        new ReleaseIdentityKey
                        {
                            Authority = "btih",
                            Value = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        },
                    },
                },
            ]);
        _releaseRepository
            .SetupSequence(repository => repository.UpdateAsync(
                It.IsAny<Release>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        _releaseRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<Release>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("The exact identity already exists."));

        // Act
        await _subject.SearchAsync(
            _contentHierarchyItemId,
            _userId,
            correlationId: null,
            currentExecutionId: null,
            progress: null).ConfigureAwait(false);

        // Assert
        _releaseRepository.Verify(repository => repository.UpdateAsync(
            It.IsAny<Release>(),
            _userId,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        _releaseRepository.Verify(repository => repository.CreateAsync(
            It.IsAny<Release>(),
            _userId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase("weak", "strong", "strong")]
    [TestCase("strong", "weak", "strong")]
    public async Task SearchAsync_EvaluatesDuplicateOnce_WhenProvidersReturnTheSameRelease(
        string firstTerm,
        string secondTerm,
        string positiveTerm)
    {
        // Arrange
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
        _contentFactProvider
            .Setup(value => value.GetFactsAsync(
                It.IsAny<ContentTarget>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([[new ContentFact { Value = firstTerm }], [new ContentFact { Value = secondTerm }],]);
        _factProvider
            .Setup(provider => provider.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns((
                ContentTarget _,
                IReadOnlyList<IReadOnlyList<ContentFact>> _,
                Release _,
                IReadOnlyList<ContentFact> facts) => facts.Any(fact => fact.Value == positiveTerm)
                    ? ReleaseMatchResult.Confident
                    : ReleaseMatchResult.NoMatch);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.Is<ReleaseSearchRequest>(request => request.Title == firstTerm),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "The Lion Tamer",
                    ExternalId = "same-release",
                },
            ]);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.Is<ReleaseSearchRequest>(request => request.Title == secondTerm),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "The Lion Tamer",
                    ExternalId = "same-release",
                    IdentityKeys =
                    {
                        new ReleaseIdentityKey { Authority = "btih", Value = "abc123" },
                    },
                },
            ]);

        // Act
        await _subject.SearchAsync(
            _contentHierarchyItemId,
            _userId,
            correlationId: null,
            currentExecutionId: null,
            progress: null).ConfigureAwait(false);

        // Assert
        _releaseRepository.Verify(
            repository => repository.CreateAsync(
                It.IsAny<Release>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _releaseEvaluationService.Verify(
            service => service.ResolveScopeAsync(
                It.IsAny<Release>(),
                It.Is<IReadOnlyList<ContentFact>>(facts =>
                    facts.Select(fact => fact.Value).SequenceEqual(new[] { firstTerm, secondTerm })),
                _contentHierarchyItemId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _releaseEvaluationService.Verify(
            service => service.AssessMatch(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<ReleaseEvaluationContext>()),
            Times.Once);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task SearchAsync_PreservesStrongestScopeAndAllSources_WhenConnectionOrderVaries(bool strongFirst)
    {
        // Arrange
        var secondIndexerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var secondConnectionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var releaseId = Guid.NewGuid();
        Release? persisted = null;
        _indexers
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Indexer
                {
                    Id = _indexerId,
                    ProviderType = "test-indexer",
                    Name = "Strong Indexer",
                    CreatedAt = DateTime.UtcNow,
                },
                new Indexer
                {
                    Id = secondIndexerId,
                    ProviderType = "second-test-indexer",
                    Name = "Weak Indexer",
                    CreatedAt = DateTime.UtcNow,
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
                    Name = strongFirst ? "A Strong Connection" : "Z Strong Connection",
                    Enabled = true,
                    CreatedAt = DateTime.UtcNow,
                },
                new IndexerConnection
                {
                    Id = secondConnectionId,
                    IndexerId = secondIndexerId,
                    Name = strongFirst ? "Z Weak Connection" : "A Weak Connection",
                    Enabled = true,
                    CreatedAt = DateTime.UtcNow,
                },
            ]);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Movie.2024.1080p",
                    ExternalId = "strong-source",
                    IdentityKeys =
                    {
                        new ReleaseIdentityKey
                        {
                            Authority = "btih",
                            Value = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        },
                    },
                },
                new Release
                {
                    Protocol = "torrent",
                    Title = "Movie.2024.Alternate.1080p",
                    ExternalId = "confident-source",
                    IdentityKeys =
                    {
                        new ReleaseIdentityKey
                        {
                            Authority = "btih",
                            Value = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        },
                    },
                },
            ]);
        _secondIndexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Mangled.Not.The.Searched.Movie",
                    ExternalId = "weak-source",
                    IdentityKeys =
                    {
                        new ReleaseIdentityKey
                        {
                            Authority = "btih",
                            Value = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        },
                    },
                },
            ]);
        _factProvider
            .Setup(provider => provider.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.Is<Release>(release => release.Title == "Movie.2024.1080p"),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(ReleaseMatchResult.Identity);
        _factProvider
            .Setup(provider => provider.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.Is<Release>(release => release.Title == "Movie.2024.Alternate.1080p"),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(ReleaseMatchResult.Confident);
        _factProvider
            .Setup(provider => provider.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.Is<Release>(release => release.Title == "Mangled.Not.The.Searched.Movie"),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(ReleaseMatchResult.Indeterminate);
        _releaseRepository
            .Setup(repository => repository.UpdateAsync(
                It.IsAny<Release>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release release, Guid? _, CancellationToken _) =>
            {
                if (persisted is null)
                {
                    return false;
                }

                release.Id = persisted.Id;
                persisted = release;
                return true;
            });
        _releaseRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<Release>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release release, Guid? _, CancellationToken _) =>
            {
                release.Id = releaseId;
                persisted = release;
                return release;
            });
        _releaseEvaluationService
            .Setup(service => service.ResolveScopeAsync(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                _contentHierarchyItemId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                Release release,
                IReadOnlyList<ContentFact> _,
                Guid _,
                CancellationToken _) => release.Title == "Movie.2024.1080p"
                    ? new ReleaseScopeResolution([_contentHierarchyItemId], [_contentHierarchyItemId])
                    : new ReleaseScopeResolution([], []));

        // Act
        await _subject.SearchAsync(
            _contentHierarchyItemId,
            _userId,
            correlationId: null,
            currentExecutionId: null,
            progress: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted, Is.Not.Null);
            Assert.That(persisted!.Title, Is.EqualTo("Movie.2024.1080p"));
            Assert.That(
                persisted.Sources.Select(source => source.ExternalId),
                Is.EquivalentTo(["strong-source", "confident-source", "weak-source"]));
        }

        _releaseEvaluationService.Verify(service => service.ResolveScopeAsync(
            It.Is<Release>(release => release.Title == "Movie.2024.1080p"),
            It.IsAny<IReadOnlyList<ContentFact>>(),
            _contentHierarchyItemId,
            It.IsAny<CancellationToken>()), Times.Once);
        _releaseScopeRepository.Verify(repository => repository.CreateAsync(
            It.Is<ReleaseScope>(scope =>
                scope.ReleaseId == releaseId &&
                scope.ContentHierarchyItemId == _contentHierarchyItemId),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(true, FulfillmentStatus.Fulfilled)]
    [TestCase(true, FulfillmentStatus.FulfilledExternally)]
    [TestCase(false, FulfillmentStatus.Missing)]
    public async Task SearchAsync_QueriesNoIndexer_WhenLeafDoesNotNeedAcquisition(
        bool effectiveMonitoring,
        FulfillmentStatus fulfillment)
    {
        // Arrange
        _hierarchyItem.EffectiveMonitoring = effectiveMonitoring;
        _hierarchyItem.ContentItem = new ContentItem
        {
            Id = _contentItemId,
            ContentHierarchyItemId = _contentHierarchyItemId,
            Fulfillment = fulfillment,
            CreatedAt = DateTime.UtcNow,
        };
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
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress: null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _indexerIntegration.Verify(
                integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _contentRepository.Verify(
                r => r.UpdateAsync(
                    It.IsAny<ContentItem>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SearchAsync_QueriesTheIndexer_WhenAFulfilledLeafIsSearchedImmediately()
    {
        // Arrange
        _hierarchyItem.ContentItem = new ContentItem
        {
            Id = _contentItemId,
            ContentHierarchyItemId = _contentHierarchyItemId,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow,
        };
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
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress: null,
                immediateOverride: true)
            .ConfigureAwait(false);

        // Assert
        _indexerIntegration.Verify(
            integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task SearchAsync_Searches_WhenRecentCompletedExecutionWasSkipped()
    {
        // Arrange
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q =>
                    q.JobType == "ReleaseSearchItem" &&
                    q.Statuses != null &&
                    q.Statuses.Contains(JobExecutionStatus.Completed)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        Id = Guid.NewGuid(),
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Completed,
                        StatusMessage = "Skipped: a recent search for this item completed " +
                            "inside the coalescing window.",
                        CreatedAt = DateTime.UtcNow,
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] =
                                _contentHierarchyItemId.ToString("D"),
                        },
                    },
                ],
            });

        // Act
        await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress: null)
            .ConfigureAwait(false);

        // Assert
        _contentHierarchyItems.Verify(
            repository => repository.GetByIdAsync(
                _contentHierarchyItemId,
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task SearchAsync_Skips_WhenRecentExecutionCompletedASearch()
    {
        // Arrange
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q =>
                    q.JobType == "ReleaseSearchItem" &&
                    q.Statuses != null &&
                    q.Statuses.Contains(JobExecutionStatus.Completed)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        Id = Guid.NewGuid(),
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Completed,
                        StatusMessage = "Release search complete.",
                        CreatedAt = DateTime.UtcNow,
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] =
                                _contentHierarchyItemId.ToString("D"),
                        },
                    },
                ],
            });

        // Act
        await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress: null)
            .ConfigureAwait(false);

        // Assert
        _contentHierarchyItems.Verify(
            repository => repository.GetByIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task SearchAsync_Skips_WhenARecentExecutionCompletedASearchAndTheSearchIsForced()
    {
        // Arrange
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q =>
                    q.JobType == "ReleaseSearchItem" &&
                    q.Statuses != null &&
                    q.Statuses.Contains(JobExecutionStatus.Completed)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        Id = Guid.NewGuid(),
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Completed,
                        StatusMessage = "Release search complete.",
                        CreatedAt = DateTime.UtcNow,
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] =
                                _contentHierarchyItemId.ToString("D"),
                        },
                    },
                ],
            });

        // Act
        await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress: null,
                immediateOverride: true)
            .ConfigureAwait(false);

        // Assert
        _contentHierarchyItems.Verify(
            repository => repository.GetByIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task SearchAsync_ReportsASkippedStatus_WhenARecentExecutionCompletedASearch()
    {
        // Arrange
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q =>
                    q.JobType == "ReleaseSearchItem" &&
                    q.Statuses != null &&
                    q.Statuses.Contains(JobExecutionStatus.Completed)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        Id = Guid.NewGuid(),
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Completed,
                        StatusMessage = "Release search complete.",
                        CreatedAt = DateTime.UtcNow,
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] =
                                _contentHierarchyItemId.ToString("D"),
                        },
                    },
                ],
            });
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress.Object)
            .ConfigureAwait(false);

        // Assert
        progress.Verify(
            value => value.Report(
                1,
                "Skipped: a recent search for this item completed inside the coalescing window."),
            Times.Once);
    }

    [Test]
    public async Task SearchAsync_KeepsScopeRowsItDidNotObserve_WhenOneConnectionFails()
    {
        // Arrange
        var secondIndexerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var secondConnectionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var staleScopeId = Guid.NewGuid();
        _indexers
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Indexer
                {
                    Id = _indexerId,
                    ProviderType = "test-indexer",
                    Name = "Healthy Indexer",
                    CreatedAt = DateTime.UtcNow,
                },
                new Indexer
                {
                    Id = secondIndexerId,
                    ProviderType = "second-test-indexer",
                    Name = "Failing Indexer",
                    CreatedAt = DateTime.UtcNow,
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
                    Name = "Healthy Connection",
                    Enabled = true,
                    CreatedAt = DateTime.UtcNow,
                },
                new IndexerConnection
                {
                    Id = secondConnectionId,
                    IndexerId = secondIndexerId,
                    Name = "Failing Connection",
                    Enabled = true,
                    CreatedAt = DateTime.UtcNow,
                },
            ]);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _secondIndexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The indexer refused the query."));
        _releaseScopeRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseScopeQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ReleaseScope>
            {
                Items =
                [
                    new ReleaseScope
                    {
                        Id = staleScopeId,
                        ContentHierarchyItemId = _contentHierarchyItemId,
                        ReleaseId = Guid.NewGuid(),
                    },
                ],
            });

        // Act
        await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress: null)
            .ConfigureAwait(false);

        // Assert
        _releaseScopeRepository.Verify(
            repository => repository.DeleteAsync(
                staleScopeId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void SearchAsync_FailsWithoutMarkingSearched_WhenEveryAttemptedConnectionFails()
    {
        // Arrange
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
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("indexer unreachable"));

        // Act
        async Task Act() => await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress: null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.ThrowsAsync<InvalidOperationException>(Act);
            _contentRepository.Verify(
                r => r.UpdateAsync(
                    It.IsAny<ContentItem>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _releaseRepository.Verify(
                r => r.CreateAsync(It.IsAny<Release>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _jobManager.Verify(
                m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SearchAsync_CompletesNoOpWithoutMarkingSearched_WhenNoConnectionIsAttempted()
    {
        // Arrange
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
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress.Object)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _indexerIntegration.Verify(
                integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _contentRepository.Verify(
                r => r.UpdateAsync(
                    It.IsAny<ContentItem>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _releaseScopeRepository.Verify(
                repository => repository.DeleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            progress.Verify(
                p => p.Report(
                    It.IsAny<double?>(),
                    "The search completed without changing saved results because no query executed."),
                Times.Once);
        }
    }

    [Test]
    public async Task SearchAsync_IssuesJoinedRootToLeafTitle_WhenLineageHasTitles()
    {
        // Arrange
        var showId = Guid.NewGuid();
        var showContribution = new MetadataContribution
        {
            ProviderType = "test-metadata",
            Title = "Phineas and Ferb",
            CreatedAt = DateTime.UtcNow,
        };
        var showContainer = new MetadataContainer { Id = showId, CreatedAt = DateTime.UtcNow };
        showContainer.Contributions.Add(showContribution);
        var episodeContribution = new MetadataContribution
        {
            ProviderType = "test-metadata",
            Title = "Rollercoaster",
            CreatedAt = DateTime.UtcNow,
        };
        var episodeContainer = new MetadataContainer
        {
            Id = _contentHierarchyItemId,
            CreatedAt = DateTime.UtcNow,
        };
        episodeContainer.Contributions.Add(episodeContribution);
        var lineage = new[]
        {
            new ContentHierarchyItem
            {
                Id = _contentHierarchyItemId,
                ParentId = showId,
                ContentType = "tv",
                Level = "episode",
                Slug = "rollercoaster",
                Name = "Rollercoaster",
                EffectiveMonitoring = true,
                CreatedAt = DateTime.UtcNow,
                MetadataContainerId = _contentHierarchyItemId,
                MetadataContainer = episodeContainer,
            },
            new ContentHierarchyItem
            {
                Id = showId,
                ContentType = "tv",
                Level = "show",
                Slug = "phineas-and-ferb",
                Name = "Phineas and Ferb",
                CreatedAt = DateTime.UtcNow,
                MetadataContainerId = showId,
                MetadataContainer = showContainer,
            },
        };
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(_contentHierarchyItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lineage[0]);
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(_contentHierarchyItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lineage);
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(_contentHierarchyItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lineage);
        _materializedMetadataRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<MetadataCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MaterializedMetadata>
            {
                Items =
                [
                    new MaterializedMetadata
                    {
                        ContainerId = _contentHierarchyItemId,
                        Title = "Rollercoaster",
                    },
                    new MaterializedMetadata
                    {
                        ContainerId = showId,
                        Title = "Phineas and Ferb",
                    },
                ],
            });
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
                    Status = IntegrationHealthStatus.Healthy,
                    CreatedAt = DateTime.UtcNow,
                },
            ]);
        var requests = new List<ReleaseSearchRequest>();
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                requests.Add(request))
            .ReturnsAsync([]);

        // Act
        await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress: null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests.Select(request => request.Title), Does.Contain("Phineas and Ferb Rollercoaster"));
            Assert.That(
                requests.Select(request => request.Title ?? string.Empty),
                Has.None.Matches<string>(title => title.Contains('"')));
        }
    }

    [TestCase("imdb", "imdbid")]
    [TestCase("customsource", "customsource")]
    public async Task SearchAsync_IssuesIdQuery_WhenTargetExternalIdFacetAdvertised(string source, string facetKey)
    {
        // Arrange
        _metadataContribution.ExternalIds.Add(new ExternalId { Source = source, Value = "tt1160419" });
        _indexerIntegration
            .SetupGet(integration => integration.SearchFacets)
            .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { facetKey },]);
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
                    Status = IntegrationHealthStatus.Healthy,
                    CreatedAt = DateTime.UtcNow,
                },
            ]);
        var requests = new List<ReleaseSearchRequest>();
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                requests.Add(request))
            .ReturnsAsync([]);

        // Act
        await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(
            requests,
            Has.Some.Matches<ReleaseSearchRequest>(request =>
                request.IdSearchParameter == facetKey &&
                request.IdSearchValue == "tt1160419" &&
                request.IdSearchSource == source &&
                request.IdSearchIdentityValue == "tt1160419"));
    }

    [Test]
    public async Task SearchAsync_IssuesEachIndexerItsOwnSpelling_WhenAdvertisedFacetSpellingsDiffer()
    {
        // Arrange
        var secondIndexerId = Guid.NewGuid();
        _metadataContribution.ExternalIds.Add(new ExternalId { Source = "abcd", Value = "abcd-1" });
        _indexerIntegration
            .SetupGet(integration => integration.SearchFacets)
            .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "abcd_id" },]);
        _secondIndexerIntegration
            .SetupGet(integration => integration.SearchFacets)
            .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "abcdid" },]);
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
                new Indexer
                {
                    Id = secondIndexerId,
                    ProviderType = "second-test-indexer",
                    Name = "Second Test Indexer",
                    CreatedAt = DateTime.UtcNow,
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
                    Name = "Test Connection",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    CreatedAt = DateTime.UtcNow,
                },
                new IndexerConnection
                {
                    Id = Guid.NewGuid(),
                    IndexerId = secondIndexerId,
                    Name = "Second Test Connection",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    CreatedAt = DateTime.UtcNow,
                },
            ]);
        var firstRequests = new List<ReleaseSearchRequest>();
        var secondRequests = new List<ReleaseSearchRequest>();
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                firstRequests.Add(request))
            .ReturnsAsync([]);
        _secondIndexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                secondRequests.Add(request))
            .ReturnsAsync([]);

        // Act
        await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress: null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                firstRequests,
                Has.Some.Matches<ReleaseSearchRequest>(request =>
                    request.IdSearchParameter == "abcd_id" &&
                    request.IdSearchValue == "abcd-1"));
            Assert.That(
                secondRequests,
                Has.Some.Matches<ReleaseSearchRequest>(request =>
                    request.IdSearchParameter == "abcdid" &&
                    request.IdSearchValue == "abcd-1"));
            Assert.That(firstRequests.Select(request => request.IdSearchParameter), Has.None.EqualTo("abcdid"));
            Assert.That(secondRequests.Select(request => request.IdSearchParameter), Has.None.EqualTo("abcd_id"));
        }
    }

    [Test]
    public async Task SearchAsync_OmitsIdAlternative_WhenFacetNotAdvertised()
    {
        // Arrange
        _metadataContribution.ExternalIds.Add(new ExternalId { Source = "customsource", Value = "tt1160419" });
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
                    Status = IntegrationHealthStatus.Healthy,
                    CreatedAt = DateTime.UtcNow,
                },
            ]);
        var requests = new List<ReleaseSearchRequest>();
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                requests.Add(request))
            .ReturnsAsync([]);

        // Act
        await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress: null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests, Is.Not.Empty);
            Assert.That(requests.Any(request => request.IdSearchParameter is not null), Is.False);
            Assert.That(requests.Any(request => request.Criteria.ContainsKey("customsource")), Is.False);
            Assert.That(requests.Any(request => request.Title is not null), Is.True);
        }
    }

    [Test]
    public async Task SearchAsync_SkipsUnhealthyConnectionAtExecution_WhenConnectionStillContributesPlanningCapability()
    {
        // Arrange
        var unhealthyConnectionId = Guid.NewGuid();
        var healthyConnectionId = Guid.NewGuid();
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
        _indexerConnections
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new IndexerConnection
                {
                    Id = unhealthyConnectionId,
                    IndexerId = _indexerId,
                    Name = "Unhealthy Connection",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Unhealthy,
                    Settings = new Dictionary<string, string> { ["path"] = "unhealthy" },
                    CreatedAt = DateTime.UtcNow,
                },
                new IndexerConnection
                {
                    Id = healthyConnectionId,
                    IndexerId = _indexerId,
                    Name = "Healthy Connection",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    Settings = new Dictionary<string, string> { ["path"] = "healthy" },
                    CreatedAt = DateTime.UtcNow,
                },
            ]);
        var settingsPaths = new List<string>();
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest _, IReadOnlyDictionary<string, string> settings, CancellationToken _) =>
                settingsPaths.Add(settings["path"]))
            .ReturnsAsync([]);

        // Act
        await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress: null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _indexerIntegration.Verify(
                integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.That(settingsPaths, Is.EqualTo(["healthy"]));
        }
    }

    [Test]
    public async Task SearchInteractiveAsync_RetainsStrongestCompleteFacts_WhenPhasesReturnSameRelease()
    {
        // Arrange
        _metadataContribution.ExternalIds.Add(new ExternalId { Source = "imdb", Value = "tt1160419" });
        _indexerIntegration
            .SetupGet(integration => integration.SearchFacets)
            .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "imdbid" },]);
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
                    Status = IntegrationHealthStatus.Healthy,
                    CreatedAt = DateTime.UtcNow,
                },
            ]);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReleaseSearchRequest _, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Pilot.1080p.WEB-GRP",
                    ExternalId = "release-1",
                    PublishDate = DateTime.UtcNow,
                },
            ]);

        // Act
        var candidates = await _subject.SearchInteractiveAsync(_contentHierarchyItemId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidates, Has.Count.EqualTo(1));
            Assert.That(candidates![0].MatchResult, Is.EqualTo(ReleaseMatchResult.Identity));
            Assert.That(candidates[0].Release.ExternalIds.Select(externalId =>
                (externalId.Source, externalId.Value)), Does.Contain(("imdb", "tt1160419")));
            _indexerIntegration.Verify(
                integration => integration.SearchAsync(
                    It.Is<ReleaseSearchRequest>(request =>
                        request.IdSearchParameter == "imdbid" &&
                        request.IdSearchValue == "tt1160419" &&
                        request.IdSearchSource == "imdb" &&
                        request.IdSearchIdentityValue == "tt1160419"),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _indexerIntegration.Verify(
                integration => integration.SearchAsync(
                    It.Is<ReleaseSearchRequest>(request => request.IdSearchParameter == null),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _factProvider.Verify(
                provider => provider.IsMatch(
                    It.IsAny<ContentTarget>(),
                    It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                    It.IsAny<Release>(),
                    It.Is<IReadOnlyList<ContentFact>>(facts => facts.Count > 1)),
                Times.Never);
        }
    }

    [Test]
    public async Task SearchInteractiveAsync_UsesFirstDecisiveReleaseComparer_WhenPrimaryRanksTie()
    {
        // Arrange
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
                    Status = IntegrationHealthStatus.Healthy,
                    CreatedAt = DateTime.UtcNow,
                },
            ]);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Pilot",
                    ExternalId = "court-ordered-smoothie",
                    PublishDate = new DateTime(2025, 1, 1),
                },
                new Release
                {
                    Protocol = "torrent",
                    Title = "Pilot",
                    ExternalId = "lion-tamer",
                    PublishDate = new DateTime(2026, 1, 1),
                },
            ]);
        _firstReleaseComparer
            .Setup(comparer => comparer.Compare(
                It.Is<Release>(release => release.ExternalId == "court-ordered-smoothie"),
                It.Is<Release>(release => release.ExternalId == "lion-tamer")))
            .Returns(-1);
        _firstReleaseComparer
            .Setup(comparer => comparer.Compare(
                It.Is<Release>(release => release.ExternalId == "lion-tamer"),
                It.Is<Release>(release => release.ExternalId == "court-ordered-smoothie")))
            .Returns(1);

        // Act
        var candidates = await _subject.SearchInteractiveAsync(_contentHierarchyItemId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidates![0].Release.ExternalId, Is.EqualTo("court-ordered-smoothie"));
            _firstReleaseComparer.Verify(
                comparer => comparer.Compare(It.IsAny<Release>(), It.IsAny<Release>()),
                Times.AtLeastOnce);
            _secondReleaseComparer.Verify(
                comparer => comparer.Compare(It.IsAny<Release>(), It.IsAny<Release>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SearchInteractiveAsync_UsesDeterministicFallback_WhenComparerCannotCompareEveryCandidate()
    {
        // Arrange
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
                    Status = IntegrationHealthStatus.Healthy,
                    CreatedAt = DateTime.UtcNow,
                },
            ]);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Pilot",
                    ExternalId = "court-ordered-smoothie",
                    PublishDate = new DateTime(2025, 1, 1),
                },
                new Release
                {
                    Protocol = "torrent",
                    Title = "Pilot",
                    ExternalId = "lion-tamer",
                    PublishDate = new DateTime(2026, 1, 1),
                },
                new Release
                {
                    Protocol = "torrent",
                    Title = "Pilot",
                    ExternalId = "claudfather",
                    PublishDate = new DateTime(2024, 1, 1),
                },
            ]);
        _firstReleaseComparer
            .Setup(comparer => comparer.CanCompare(It.Is<Release>(release => release.ExternalId == "claudfather")))
            .Returns(false);
        _firstReleaseComparer
            .Setup(comparer => comparer.Compare(
                It.Is<Release>(release => release.ExternalId == "court-ordered-smoothie"),
                It.Is<Release>(release => release.ExternalId == "lion-tamer")))
            .Returns(-1);
        _firstReleaseComparer
            .Setup(comparer => comparer.Compare(
                It.Is<Release>(release => release.ExternalId == "lion-tamer"),
                It.Is<Release>(release => release.ExternalId == "claudfather")))
            .Returns(-1);
        _firstReleaseComparer
            .Setup(comparer => comparer.Compare(
                It.Is<Release>(release => release.ExternalId == "claudfather"),
                It.Is<Release>(release => release.ExternalId == "court-ordered-smoothie")))
            .Returns(-1);

        // Act
        var candidates = await _subject.SearchInteractiveAsync(_contentHierarchyItemId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                candidates!.Select(candidate => candidate.Release.ExternalId),
                Is.EqualTo(["lion-tamer", "court-ordered-smoothie", "claudfather"]));
            _firstReleaseComparer.Verify(
                comparer => comparer.Compare(It.IsAny<Release>(), It.IsAny<Release>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SearchAsync_FansOutToChildren_WhenNodeIsNonLeaf()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var nonLeafChildId = Guid.NewGuid();
        var grandchildLeafId = Guid.NewGuid();
        var leafChildId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = parentId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(parentId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [parentId] = 2 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId == parentId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = nonLeafChildId,
                        ParentId = parentId,
                        ContentType = "tv",
                        Level = "season",
                        Slug = "s1",
                        Name = "Season 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                    new ContentHierarchyItem
                    {
                        Id = grandchildLeafId,
                        ParentId = nonLeafChildId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e2",
                        Name = "Episode 2",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                    new ContentHierarchyItem
                    {
                        Id = leafChildId,
                        ParentId = parentId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                ],
            });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.LeafOnly == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafChildId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = leafChildId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = null,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });

        // Act
        await _subject
            .SearchAsync(parentId, _userId, correlationId: null, currentExecutionId: null, progress: null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r =>
                        r.JobType == "ReleaseSearchItem" &&
                        r.Parameters != null &&
                        r.Parameters["contentHierarchyItemId"] ==
                            nonLeafChildId.ToString("D") &&
                        r.Parameters["searchKind"] ==
                            "node"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r =>
                        r.JobType == "ReleaseSearchItem" &&
                        r.Parameters != null &&
                        r.Parameters["contentHierarchyItemId"] ==
                            leafChildId.ToString("D") &&
                        r.Parameters["searchKind"] ==
                            "leaf"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentService.Verify(
                r => r.GetOrCreateContentItemAsync(
                    parentId,
                    It.IsAny<DateTime>(),
                    It.IsAny<Guid?>(),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SearchAsync_ReportsTheQueuedChildSearchCount_WhenTheNodeFansOut()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var firstLeafId = Guid.NewGuid();
        var secondLeafId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = parentId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(parentId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [parentId] = 2 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId == parentId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = firstLeafId,
                        ParentId = parentId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                    new ContentHierarchyItem
                    {
                        Id = secondLeafId,
                        ParentId = parentId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e2",
                        Name = "Episode 2",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                ],
            });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.LeafOnly == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = firstLeafId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = firstLeafId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = null,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                    new ContentHierarchyItem
                    {
                        Id = secondLeafId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e2",
                        Name = "Episode 2",
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = secondLeafId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = null,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(parentId, _userId, correlationId: null, currentExecutionId: null, progress.Object)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            progress.Verify(p => p.Report(It.IsAny<double?>(), "The search queued 2 child searches."), Times.Once);
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r => r.JobType == "ReleaseSearchItem"),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
    }

    [Test]
    public async Task SearchAsync_DoesNotQueueLeafSearch_WhenActiveLeafSearchesOlderThanOneHourExceedThreshold()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var leafChildId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = parentId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(parentId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [parentId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId == parentId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafChildId,
                        ParentId = parentId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                ],
            });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.LeafOnly == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafChildId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = leafChildId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = null,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q =>
                    q.JobType == "ReleaseSearchItem" &&
                    q.Statuses != null &&
                    q.Timestamp == null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items = Enumerable.Range(0, 51)
                    .Select(_ => new JobExecution
                    {
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Queued,
                        CreatedAt = DateTime.UtcNow.AddHours(-2),
                    })
                    .ToList(),
            });

        // Act
        await _subject
            .SearchAsync(parentId, _userId, correlationId: null, currentExecutionId: null, progress: null)
            .ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.JobType == "ReleaseSearchItem" &&
                    r.Parameters != null &&
                    r.Parameters["searchKind"] ==
                        "leaf"),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task SearchAsync_QueuesLeafSearch_WhenRunningLeafSearchesHaveNoRecentHeartbeat()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var leafChildId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = parentId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(parentId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [parentId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId == parentId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafChildId,
                        ParentId = parentId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                ],
            });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.LeafOnly == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafChildId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = leafChildId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = null,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q =>
                    q.JobType == "ReleaseSearchItem" &&
                    q.Statuses != null &&
                    q.Timestamp == null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items = Enumerable.Range(0, 51)
                    .Select(_ => new JobExecution
                    {
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Running,
                        LastHeartbeat = DateTime.UtcNow.AddHours(-2),
                        CreatedAt = DateTime.UtcNow.AddHours(-2),
                    })
                    .ToList(),
            });

        // Act
        await _subject
            .SearchAsync(parentId, _userId, correlationId: null, currentExecutionId: null, progress: null)
            .ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.JobType == "ReleaseSearchItem" &&
                    r.Parameters != null &&
                    r.Parameters["searchKind"] ==
                        "leaf"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task SearchAsync_DoesNotQueueLeafSearch_WhenRecentSearchFoundNoReleases()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var leafChildId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = parentId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(parentId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [parentId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.LeafOnly == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafChildId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = leafChildId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });

        // Act
        await _subject
            .SearchAsync(parentId, _userId, correlationId: null, currentExecutionId: null, progress: null)
            .ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task SearchAsync_QueuesLeafSearch_WhenEveryStoredReleaseHasFailed()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Pilot.1080p-GRP",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "failed-key" } },
        };
        _contentHierarchyItems
            .Setup(repository => repository.GetByIdAsync(parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = parentId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(repository => repository.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(parentId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [parentId] = 1 });
        _contentHierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.LeafOnly == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = _contentHierarchyItemId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "pilot",
                        Name = "Pilot",
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = _contentItemId,
                            ContentHierarchyItemId = _contentHierarchyItemId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        _releaseRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<ReleaseQuery>(query => query.ContentHierarchyItemId == _contentHierarchyItemId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [release], TotalItems = 1 });
        _downloadRepository
            .Setup(repository => repository.ListFailedByReleaseIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<ReleaseIdentityKey>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Download
                {
                    Id = Guid.NewGuid(),
                    ReleaseId = release.Id,
                    DownloadClientId = Guid.NewGuid(),
                    ExternalId = string.Empty,
                    Title = "failed grab",
                    Status = DownloadStatus.Failed,
                    CreatedAt = DateTime.UtcNow,
                    ReleaseIdentityKeys =
                    {
                        new ReleaseIdentityKey { Authority = "provider", Value = "failed-key" },
                    },
                },
            ]);

        // Act
        await _subject
            .SearchAsync(parentId, _userId, correlationId: null, currentExecutionId: null, progress: null)
            .ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.Parameters != null &&
                    request.Parameters["contentHierarchyItemId"] ==
                        _contentHierarchyItemId.ToString("D") &&
                    request.Parameters["searchKind"] ==
                        "leaf"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task SearchAsync_DefersLeafSearchesButStillEnqueuesNodeJobs_WhenLeafSearchVolumeExceedsThreshold()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var nonLeafChildId = Guid.NewGuid();
        var grandchildLeafId = Guid.NewGuid();
        var leafChildId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = parentId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(parentId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [parentId] = 2 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId == parentId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = nonLeafChildId,
                        ParentId = parentId,
                        ContentType = "tv",
                        Level = "season",
                        Slug = "s1",
                        Name = "Season 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                    new ContentHierarchyItem
                    {
                        Id = grandchildLeafId,
                        ParentId = nonLeafChildId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e2",
                        Name = "Episode 2",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                ],
            });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.LeafOnly == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafChildId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = leafChildId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = null,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items = [.. Enumerable.Range(0, 51)
                    .Select(_ => new JobExecution
                    {
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Queued,
                        CreatedAt = DateTime.UtcNow,
                        Parameters = new Dictionary<string, string>
                        {
                            ["searchKind"] =
                                "leaf",
                        },
                    })],
            });

        // Act
        await _subject
            .SearchAsync(parentId, _userId, correlationId: null, currentExecutionId: null, progress: null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r =>
                        r.Parameters != null &&
                        r.Parameters["contentHierarchyItemId"] ==
                            nonLeafChildId.ToString("D")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r =>
                        r.Parameters != null &&
                        r.Parameters["contentHierarchyItemId"] ==
                            leafChildId.ToString("D")),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SearchAsync_DoesNotReEnqueueLeaf_WhenAPriorSearchCompletedWithoutSettling()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var leafChildId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = parentId,
                ContentType = "tv",
                Level = "season",
                Slug = "s1",
                Name = "Season 1",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(parentId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [parentId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.LeafOnly == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafChildId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = leafChildId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = null,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q => q.Status == JobExecutionStatus.Completed),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Completed,
                        CreatedAt = DateTime.UtcNow,
                    },
                ],
            });

        // Act
        await _subject
            .SearchAsync(parentId, _userId, correlationId: null, currentExecutionId: null, progress: null)
            .ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task SearchAsync_TakesEscalationTurn_WhenSubtreeIsSettledWithResidual()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        var leafId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = nodeId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(nodeId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [nodeId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafId,
                        ParentId = nodeId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = leafId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(nodeId, _userId, correlationId: null, currentExecutionId: null, progress.Object)
            .ConfigureAwait(false);

        // Assert
        progress.Verify(
            p => p.Report(
                It.IsAny<double?>(),
                It.Is<string>(message => message.StartsWith("The search queued", StringComparison.Ordinal))),
            Times.Never);
    }

    [Test]
    public async Task SearchAsync_TakesEscalationTurn_WhenAResidualLeafIsUnsearchable()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        var leafId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = nodeId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(nodeId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [nodeId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafId,
                        ParentId = nodeId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = leafId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = null,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q => q.Status == JobExecutionStatus.Completed),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Completed,
                        CreatedAt = DateTime.UtcNow,
                    },
                ],
            });
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(nodeId, _userId, correlationId: null, currentExecutionId: null, progress.Object)
            .ConfigureAwait(false);

        // Assert
        progress.Verify(
            p => p.Report(
                It.IsAny<double?>(),
                It.Is<string>(message => message.StartsWith("The search queued", StringComparison.Ordinal))),
            Times.Never);
    }

    [Test]
    public async Task SearchAsync_DefersEscalation_WhenALeafSearchIsPending()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        var leafId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = nodeId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(nodeId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [nodeId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafId,
                        ParentId = nodeId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = leafId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q => q.JobType == "ReleaseSearchItem" && q.Timestamp == null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Queued,
                        CreatedAt = DateTime.UtcNow,
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] = leafId.ToString("D"),
                        },
                    },
                ],
            });
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(nodeId, _userId, correlationId: null, currentExecutionId: null, progress.Object)
            .ConfigureAwait(false);

        // Assert
        progress.Verify(p => p.Report(It.IsAny<double?>(), "The search did not queue any child searches."), Times.Once);
    }

    [Test]
    public async Task SearchAsync_DefersEscalation_WhenALeafHasNeverSettled()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        var leafId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = nodeId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(nodeId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [nodeId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafId,
                        ParentId = nodeId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = leafId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = null,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(nodeId, _userId, correlationId: null, currentExecutionId: null, progress.Object)
            .ConfigureAwait(false);

        // Assert
        progress.Verify(p => p.Report(It.IsAny<double?>(), "The search did not queue any child searches."), Times.Once);
    }

    [Test]
    public async Task SearchAsync_DefersEscalation_WhenTheOnlyResidualLeafHasAnActiveAcquisition()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        var leafId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = nodeId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(nodeId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [nodeId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafId,
                        ParentId = nodeId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = leafId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow,
                            ActiveAcquisitionStatus = DownloadStatus.Downloading,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(nodeId, _userId, correlationId: null, currentExecutionId: null, progress.Object)
            .ConfigureAwait(false);

        // Assert
        progress.Verify(p => p.Report(It.IsAny<double?>(), "The search did not queue any child searches."), Times.Once);
    }

    [Test]
    public async Task SearchAsync_DefersEscalation_WhenAResidualChildSeasonHasNotTakenItsTurn()
    {
        // Arrange
        var showId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(showId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = showId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(showId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [showId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = seasonId,
                        ParentId = showId,
                        ContentType = "tv",
                        Level = "season",
                        Slug = "s1",
                        Name = "Season 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                    new ContentHierarchyItem
                    {
                        Id = episodeId,
                        ParentId = seasonId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = episodeId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(showId, _userId, correlationId: null, currentExecutionId: null, progress.Object)
            .ConfigureAwait(false);

        // Assert
        progress.Verify(p => p.Report(It.IsAny<double?>(), "The search queued 1 child search."), Times.Once);
    }

    [Test]
    public async Task SearchAsync_DefersEscalation_WhenTheOnlyResidualLeafIsTargetedByADownloadJob()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        var leafId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = nodeId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(nodeId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [nodeId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafId,
                        ParentId = nodeId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = leafId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q => q.JobType == "ReleaseDownloadItem"),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        JobType = "ReleaseDownloadItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Queued,
                        CreatedAt = DateTime.UtcNow,
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] = leafId.ToString("D"),
                        },
                    },
                ],
            });
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(nodeId, _userId, correlationId: null, currentExecutionId: null, progress.Object)
            .ConfigureAwait(false);

        // Assert
        progress.Verify(p => p.Report(It.IsAny<double?>(), "The search did not queue any child searches."), Times.Once);
    }

    [Test]
    public async Task SearchAsync_DefersEscalation_WhenAResidualChildSeasonSearchIsPending()
    {
        // Arrange
        var showId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(showId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = showId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(showId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [showId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = seasonId,
                        ParentId = showId,
                        ContentType = "tv",
                        Level = "season",
                        Slug = "s1",
                        Name = "Season 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                    new ContentHierarchyItem
                    {
                        Id = episodeId,
                        ParentId = seasonId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = episodeId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q => q.JobType == "ReleaseSearchItem" && q.Timestamp == null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Running,
                        CreatedAt = DateTime.UtcNow,
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] = seasonId.ToString("D"),
                            ["searchKind"] =
                                "node",
                        },
                    },
                ],
            });
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(showId, _userId, correlationId: null, currentExecutionId: null, progress.Object)
            .ConfigureAwait(false);

        // Assert
        progress.Verify(p => p.Report(It.IsAny<double?>(), "The search queued 1 child search."), Times.Once);
    }

    [Test]
    public async Task SearchAsync_TakesEscalationTurn_WhenAResidualChildSeasonCompletedTurnSinceLeafBaseline()
    {
        // Arrange
        var showId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(showId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = showId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(showId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [showId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = seasonId,
                        ParentId = showId,
                        ContentType = "tv",
                        Level = "season",
                        Slug = "s1",
                        Name = "Season 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                    new ContentHierarchyItem
                    {
                        Id = episodeId,
                        ParentId = seasonId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = episodeId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q => q.Status == JobExecutionStatus.Completed && q.Timestamp != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Completed,
                        StatusMessage = "Skipped: a recent search for this item completed " +
                            "inside the coalescing window.",
                        CreatedAt = DateTime.UtcNow,
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] = seasonId.ToString("D"),
                        },
                    },
                    new JobExecution
                    {
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Completed,
                        CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] = seasonId.ToString("D"),
                        },
                    },
                ],
            });
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(showId, _userId, correlationId: null, currentExecutionId: null, progress.Object)
            .ConfigureAwait(false);

        // Assert
        progress.Verify(
            p => p.Report(
                It.IsAny<double?>(),
                It.Is<string>(message => message.StartsWith("The search queued", StringComparison.Ordinal))),
            Times.Never);
    }

    [Test]
    public async Task SearchAsync_DefersEscalation_WhenAResidualChildSeasonCompletedTurnWasSkippedSinceLeafBaseline()
    {
        // Arrange
        var showId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(showId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = showId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(showId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [showId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = seasonId,
                        ParentId = showId,
                        ContentType = "tv",
                        Level = "season",
                        Slug = "s1",
                        Name = "Season 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                    new ContentHierarchyItem
                    {
                        Id = episodeId,
                        ParentId = seasonId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = episodeId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q => q.Status == JobExecutionStatus.Completed && q.Timestamp != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Completed,
                        StatusMessage = "Skipped: a recent search for this item completed " +
                            "inside the coalescing window.",
                        CreatedAt = DateTime.UtcNow,
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] = seasonId.ToString("D"),
                        },
                    },
                ],
            });
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(showId, _userId, correlationId: null, currentExecutionId: null, progress.Object)
            .ConfigureAwait(false);

        // Assert
        progress.Verify(p => p.Report(It.IsAny<double?>(), "The search queued 1 child search."), Times.Once);
    }

    [Test]
    public async Task SearchInteractiveAsync_ReturnsNonConfidentCandidates_WhenMatchResultsAreMixed()
    {
        // Arrange
        var seasonId = Guid.NewGuid();
        var showId = Guid.NewGuid();
        var lineage4 = new[]
        {
            new ContentHierarchyItem
            {
                Id = _contentHierarchyItemId,
                ParentId = seasonId,
                ContentType = "tv",
                Level = "episode",
                Slug = "e5",
                Name = "Episode 5",
                Ordinal = 5,
                CreatedAt = DateTime.UtcNow,
                MetadataContainer = _hierarchyItem.MetadataContainer,
            },
            new ContentHierarchyItem
            {
                Id = seasonId,
                ParentId = showId,
                ContentType = "tv",
                Level = "season",
                Slug = "s1",
                Name = "Season 1",
                Ordinal = 1,
                CreatedAt = DateTime.UtcNow,
                MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            },
            new ContentHierarchyItem
            {
                Id = showId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
                MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            },
        };
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(_contentHierarchyItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lineage4);
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(_contentHierarchyItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lineage4);
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
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Show S01 SEASONPACK 1080p WEB",
                    ExternalId = "season-pack-1",
                    PublishDate = DateTime.UtcNow,
                },
                new Release
                {
                    Protocol = "torrent",
                    Title = "Show S04 Mystery Box 720p WEB",
                    ExternalId = "mystery-box-1",
                    PublishDate = DateTime.UtcNow,
                },
            ]);
        _factProvider
            .Setup(provider => provider.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.Is<Release>(release => release.ExternalId == "season-pack-1"),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(ReleaseMatchResult.NoMatch);
        _factProvider
            .Setup(provider => provider.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.Is<Release>(release => release.ExternalId == "mystery-box-1"),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(ReleaseMatchResult.Indeterminate);

        // Act
        var candidates = await _subject.SearchInteractiveAsync(_contentHierarchyItemId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                candidates,
                Has.Exactly(1).Matches<InteractiveSearchCandidate>(candidate =>
                    candidate.MatchResult == ReleaseMatchResult.NoMatch));
            Assert.That(
                candidates,
                Has.Exactly(1).Matches<InteractiveSearchCandidate>(candidate =>
                    candidate.MatchResult == ReleaseMatchResult.Indeterminate));
        }
    }

    [Test]
    public async Task SearchAsync_TakesEscalationTurn_WhenItsOwnFanOutEnqueuedTheSettledResidualChild()
    {
        // Arrange
        var showId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var leafBaseline = DateTime.UtcNow.AddMinutes(-10);
        var seasonStamp = DateTime.UtcNow.AddMinutes(-5);
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(showId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = showId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(showId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [showId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = seasonId,
                        ParentId = showId,
                        ContentType = "tv",
                        Level = "season",
                        Slug = "s1",
                        Name = "Season 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = seasonId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = seasonStamp,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                    new ContentHierarchyItem
                    {
                        Id = episodeId,
                        ParentId = seasonId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = episodeId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = leafBaseline,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q => q.JobType == "ReleaseSearchItem" && q.Timestamp == null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Queued,
                        CreatedAt = DateTime.UtcNow,
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] = seasonId.ToString("D"),
                            ["searchKind"] =
                                "node",
                        },
                    },
                ],
            });
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(showId, _userId, correlationId: null, currentExecutionId: null, progress.Object)
            .ConfigureAwait(false);

        // Assert
        progress.Verify(
            p => p.Report(
                It.IsAny<double?>(),
                It.Is<string>(message => message.StartsWith("The search queued", StringComparison.Ordinal))),
            Times.Never);
    }

    [Test]
    public async Task SearchAsync_DefersEscalation_WhenAResidualChildIsPendingFromAnExternalTrigger()
    {
        // Arrange
        var showId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var leafBaseline = DateTime.UtcNow.AddMinutes(-10);
        var seasonStamp = DateTime.UtcNow.AddMinutes(-5);
        _contentHierarchyItems
            .Setup(r => r.GetByIdAsync(showId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = showId,
                ContentType = "tv",
                Level = "show",
                Slug = "show",
                Name = "Show",
                CreatedAt = DateTime.UtcNow,
            });
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(showId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [showId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = seasonId,
                        ParentId = showId,
                        ContentType = "tv",
                        Level = "season",
                        Slug = "s1",
                        Name = "Season 1",
                        EffectiveMonitoring = false,
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = seasonId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = seasonStamp,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                    new ContentHierarchyItem
                    {
                        Id = episodeId,
                        ParentId = seasonId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            ContentHierarchyItemId = episodeId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = leafBaseline,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q => q.JobType == "ReleaseSearchItem" && q.Timestamp == null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Running,
                        CreatedAt = DateTime.UtcNow,
                        Parameters = new Dictionary<string, string>
                        {
                            ["contentHierarchyItemId"] = seasonId.ToString("D"),
                            ["searchKind"] =
                                "node",
                        },
                    },
                ],
            });
        var progress = new Mock<IProgressReporter>();

        // Act
        await _subject
            .SearchAsync(showId, _userId, correlationId: null, currentExecutionId: null, progress.Object)
            .ConfigureAwait(false);

        // Assert
        progress.Verify(p => p.Report(It.IsAny<double?>(), "The search did not queue any child searches."), Times.Once);
    }

    [Test]
    public async Task SearchInteractiveAsync_RequestsTheWholeUnpagedList_WhenTheIndexerIsQueried()
    {
        // Arrange
        _metadataContribution.ExternalIds.Add(new ExternalId { Source = "imdb", Value = "tt1160419" });
        _indexerIntegration
            .SetupGet(integration => integration.SearchFacets)
            .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "imdbid" },]);
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
                    Status = IntegrationHealthStatus.Healthy,
                    CreatedAt = DateTime.UtcNow,
                },
            ]);
        var requests = new List<ReleaseSearchRequest>();
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                requests.Add(request))
            .ReturnsAsync([]);

        // Act
        await _subject.SearchInteractiveAsync(_contentHierarchyItemId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests, Is.Not.Empty);
            Assert.That(requests.Select(request => request.MaximumResults), Has.All.EqualTo(200));
        }
    }

    [Test]
    public async Task SearchAsync_RequestsTwentyResults_WhenTheIndexerIsQueried()
    {
        // Arrange
        _metadataContribution.ExternalIds.Add(new ExternalId { Source = "imdb", Value = "tt1160419" });
        _indexerIntegration
            .SetupGet(integration => integration.SearchFacets)
            .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "imdbid" },]);
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
                    Status = IntegrationHealthStatus.Healthy,
                    CreatedAt = DateTime.UtcNow,
                },
            ]);
        var requests = new List<ReleaseSearchRequest>();
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseSearchRequest request, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                requests.Add(request))
            .ReturnsAsync([]);

        // Act
        await _subject
            .SearchAsync(
                _contentHierarchyItemId,
                _userId,
                correlationId: null,
                currentExecutionId: null,
                progress: null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests, Is.Not.Empty);
            Assert.That(requests.Select(request => request.MaximumResults), Has.All.EqualTo(20));
        }
    }

    [Test]
    public async Task SearchAsync_PublishesReleaseMatchedForTheSearchedItem_WhenTheSearchedItemIsActionable()
    {
        // Arrange
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
        _factProvider
            .Setup(provider => provider.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(ReleaseMatchResult.Identity);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Court.Ordered.Smoothie.1080p",
                    ExternalId = "smoothie-1",
                },
            ]);

        // Act
        await _subject.SearchAsync(
            _contentHierarchyItemId,
            _userId,
            correlationId: null,
            currentExecutionId: null,
            progress: null).ConfigureAwait(false);

        // Assert
        var matched = _publishedEvents.Single(published => published.Name == "release.matched");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                matched.Context["content.hierarchyItemId"],
                Is.EqualTo(_contentHierarchyItemId.ToString("D")));
            Assert.That(matched.Context["content.itemId"], Is.EqualTo(_contentItemId.ToString("D")));
            Assert.That(matched.Context["content.impact.monitoring"], Is.EqualTo("true"));
            Assert.That(
                _publishedEvents.Any(published => published.Name == "release.scopeMatched"),
                Is.False,
                "No node other than the searched item is actionable.");
        }
    }

    [Test]
    public async Task SearchAsync_CarriesTheSearchCorrelation_WhenReleaseMatchedIsPublished()
    {
        // Arrange
        var correlationId = new Guid("da4265db-da0b-4059-ad06-d096f5b6bdb5");
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
        _factProvider
            .Setup(provider => provider.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(ReleaseMatchResult.Identity);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Court.Ordered.Smoothie.1080p",
                    ExternalId = "smoothie-1",
                },
            ]);

        // Act
        await _subject.SearchAsync(
            _contentHierarchyItemId,
            _userId,
            correlationId,
            currentExecutionId: null,
            progress: null).ConfigureAwait(false);

        // Assert
        var matched = _publishedEvents.Single(published => published.Name == "release.matched");
        Assert.That(matched.CorrelationId, Is.EqualTo(correlationId));
    }

    [Test]
    public async Task SearchAsync_PublishesNoReleaseMatched_WhenTheSearchedItemIsNotActionable()
    {
        // Arrange
        var scopeNodeId = new Guid("57a07789-529a-4bf4-838f-ff4bcba3dd31");
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
        _factProvider
            .Setup(provider => provider.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(ReleaseMatchResult.Identity);
        _releaseEvaluationService
            .Setup(value => value.ResolveScopeAsync(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReleaseScopeResolution([_contentHierarchyItemId], [scopeNodeId]));
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Court.Ordered.Smoothie.1080p",
                    ExternalId = "smoothie-1",
                },
            ]);

        // Act
        await _subject.SearchAsync(
            _contentHierarchyItemId,
            _userId,
            correlationId: null,
            currentExecutionId: null,
            progress: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _publishedEvents.Any(published => published.Name == "release.matched"),
                Is.False,
                "The searched item is not in the actionable set.");
            Assert.That(
                _publishedEvents
                    .Single(published => published.Name == "release.scopeMatched")
                    .Context["content.impact.batchIds"],
                Is.EqualTo("57a07789-529a-4bf4-838f-ff4bcba3dd31"));
        }
    }

    [Test]
    public async Task SearchAsync_PublishesReleaseScopeMatchedForTheOtherNodes_WhenAPackCoversMoreThanTheSearchedItem()
    {
        // Arrange
        var firstScopeNodeId = new Guid("57a07789-529a-4bf4-838f-ff4bcba3dd31");
        var secondScopeNodeId = new Guid("4cb33cc4-71a5-49f7-b362-1c988da1964e");
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
        _factProvider
            .Setup(provider => provider.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(ReleaseMatchResult.Identity);
        _releaseEvaluationService
            .Setup(value => value.ResolveScopeAsync(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReleaseScopeResolution(
                [_contentHierarchyItemId, firstScopeNodeId, secondScopeNodeId],
                [_contentHierarchyItemId, firstScopeNodeId, secondScopeNodeId]));
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Court.Ordered.Smoothie.S01.1080p",
                    ExternalId = "smoothie-s01",
                },
            ]);

        // Act
        await _subject.SearchAsync(
            _contentHierarchyItemId,
            _userId,
            correlationId: null,
            currentExecutionId: null,
            progress: null).ConfigureAwait(false);

        // Assert
        var scopeMatched = _publishedEvents.Single(published => published.Name == "release.scopeMatched");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                scopeMatched.Context["content.impact.batchIds"].Split(','),
                Is.EquivalentTo(
                [
                    "57a07789-529a-4bf4-838f-ff4bcba3dd31",
                    "4cb33cc4-71a5-49f7-b362-1c988da1964e",
                ]));
            Assert.That(scopeMatched.Context, Does.Not.ContainKey("content.hierarchyItemId"));
            Assert.That(
                _publishedEvents,
                Has.Exactly(1).Matches<MagpieEvent>(published => published.Name == "release.matched"),
                "The searched item stays the owner of the match.");
        }
    }

    [Test]
    public async Task SearchAsync_PublishesNoMatchEvent_WhenTheTurnIsAnEscalation()
    {
        // Arrange
        var leafId = new Guid("0a8b36f3-16a6-457f-8652-3fd5bfe8537b");
        _contentHierarchyItems
            .Setup(r => r.GetChildCountsByParentAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(_contentHierarchyItemId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [_contentHierarchyItemId] = 1 });
        _contentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AncestorId != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafId,
                        ParentId = _contentHierarchyItemId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "e1",
                        Name = "Episode 1",
                        EffectiveMonitoring = true,
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = new Guid("7731f095-e792-4011-8206-391b3df64df3"),
                            ContentHierarchyItemId = leafId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow,
                        },
                    },
                ],
            });
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
        _factProvider
            .Setup(provider => provider.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(ReleaseMatchResult.Identity);
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Court.Ordered.Smoothie.S01.1080p",
                    ExternalId = "smoothie-s01",
                },
            ]);

        // Act
        await _subject.SearchAsync(
            _contentHierarchyItemId,
            _userId,
            correlationId: null,
            currentExecutionId: null,
            progress: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _publishedEvents.Any(published => published.Name == "release.matched"),
                Is.False,
                "An escalation turn has no actionable anchor.");
            Assert.That(
                _publishedEvents.Any(published => published.Name == "release.scopeMatched"),
                Is.False,
                "An escalation turn has no actionable scope.");
            Assert.That(
                _publishedEvents.Any(published => published.Name == "releaseSearch.completed"),
                Is.True);
        }
    }

    [Test]
    public async Task SearchAsync_EnqueuesNoAutoGrab_WhenAPackCoversMoreThanTheSearchedItem()
    {
        // Arrange
        var firstScopeNodeId = new Guid("57a07789-529a-4bf4-838f-ff4bcba3dd31");
        var secondScopeNodeId = new Guid("4cb33cc4-71a5-49f7-b362-1c988da1964e");
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
        _factProvider
            .Setup(provider => provider.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(ReleaseMatchResult.Identity);
        _releaseEvaluationService
            .Setup(value => value.ResolveScopeAsync(
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReleaseScopeResolution(
                [_contentHierarchyItemId, firstScopeNodeId, secondScopeNodeId],
                [_contentHierarchyItemId, firstScopeNodeId, secondScopeNodeId]));
        _contentService
            .Setup(service => service.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new ContentHierarchyItem
            {
                Id = id,
                ContentType = "tv",
                Level = "episode",
                Slug = "scoped",
                Name = "Scoped",
                EffectiveMonitoring = true,
                CreatedAt = DateTime.UtcNow,
            });
        _contentService
            .Setup(service => service.GetOrCreateContentItemAsync(
                It.IsIn(firstScopeNodeId, secondScopeNodeId),
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, DateTime _, Guid? _, Guid? _, CancellationToken _) => new ContentItem
            {
                Id = id,
                ContentHierarchyItemId = id,
                Fulfillment = FulfillmentStatus.Missing,
                CreatedAt = DateTime.UtcNow,
            });
        _contentRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem>
            {
                Items =
                [
                    new ContentItem
                    {
                        Id = _contentItemId,
                        ContentHierarchyItemId = _contentHierarchyItemId,
                        Fulfillment = FulfillmentStatus.Missing,
                        CreatedAt = DateTime.UtcNow,
                    },
                ],
            });
        _indexerIntegration
            .Setup(integration => integration.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Court.Ordered.Smoothie.S01.1080p",
                    ExternalId = "smoothie-s01",
                },
            ]);

        // Act
        await _subject.SearchAsync(
            _contentHierarchyItemId,
            _userId,
            correlationId: null,
            currentExecutionId: null,
            progress: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r => r.JobType == "ReleaseAutoGrabItem"),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _contentService.Verify(
                service => service.GetOrCreateContentItemAsync(
                    firstScopeNodeId,
                    It.IsAny<DateTime>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task GrabInteractiveAsync_ReturnsAlreadyInProgress_WhenAReleaseDownloadIsQueuedOrRunning()
    {
        // Arrange
        var releaseId = Guid.NewGuid();
        _hierarchyItem.EffectiveQualityTierListId = Guid.NewGuid();
        _hierarchyItem.ContentItem = new ContentItem { Id = _contentItemId };
        _downloadClients
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new DownloadClient { Id = Guid.NewGuid(), Name = "Client", ProviderType = "test", Enabled = true }
            ]);
        _releaseRepository
            .Setup(r => r.UpdateAsync(It.IsAny<Release>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release release, Guid? _, CancellationToken _) =>
            {
                release.Id = releaseId;
                return true;
            });
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(query => query.JobType == "ReleaseDownloadItem"),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        Id = Guid.NewGuid(),
                        JobType = "ReleaseDownloadItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Running,
                        CreatedAt = DateTime.UtcNow,
                        Parameters = new Dictionary<string, string> { ["releaseId"] = releaseId.ToString() },
                    }
                ],
                TotalItems = 1,
            });

        // Act
        var outcome = await _subject.GrabInteractiveAsync(
            _contentHierarchyItemId, new Release { Title = "Pilot", Protocol = "torrent" }, _userId)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(InteractiveReleaseGrabResult.AlreadyInProgress));
            _jobManager.Verify(
                m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task GrabInteractiveAsync_ReturnsAlreadyInProgress_WhenTheEnqueueCoalescedOntoAnotherExecution()
    {
        // Arrange
        var releaseId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        _hierarchyItem.EffectiveQualityTierListId = Guid.NewGuid();
        _hierarchyItem.ContentItem = new ContentItem { Id = _contentItemId };
        _downloadClients
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new DownloadClient { Id = Guid.NewGuid(), Name = "Client", ProviderType = "test", Enabled = true }
            ]);
        _releaseRepository
            .Setup(r => r.UpdateAsync(It.IsAny<Release>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release release, Guid? _, CancellationToken _) =>
            {
                release.Id = releaseId;
                return true;
            });
        _jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(executionId);
        _jobExecutions
            .Setup(r => r.GetByIdAsync(executionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobExecution
            {
                Id = executionId,
                JobType = "ReleaseDownloadItem",
                Scope = JobScope.Content,
                Status = JobExecutionStatus.Queued,
                CreatedAt = DateTime.UtcNow,
                CorrelationId = Guid.NewGuid(),
            });

        // Act
        var outcome = await _subject.GrabInteractiveAsync(
            _contentHierarchyItemId, new Release { Title = "Pilot", Protocol = "torrent" }, _userId)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(InteractiveReleaseGrabResult.AlreadyInProgress));
            Assert.That(outcome.ExecutionId, Is.EqualTo(executionId));
        }
    }

    [Test]
    public async Task GrabInteractiveAsync_QueuesReleaseDownloadItem_WhenNoDownloadIsActive()
    {
        // Arrange
        var releaseId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        _hierarchyItem.EffectiveQualityTierListId = Guid.NewGuid();
        _hierarchyItem.ContentItem = new ContentItem { Id = _contentItemId };
        _downloadClients
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new DownloadClient { Id = Guid.NewGuid(), Name = "Client", ProviderType = "test", Enabled = true }
            ]);
        _releaseRepository
            .Setup(r => r.UpdateAsync(It.IsAny<Release>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release release, Guid? _, CancellationToken _) =>
            {
                release.Id = releaseId;
                return true;
            });
        _jobManager
            .Setup(m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "ReleaseDownloadItem" &&
                    request.Mode == JobExecutionMode.Queue &&
                    request.UserId == _userId &&
                    request.Parameters!["releaseId"] == releaseId.ToString() &&
                    request.Parameters!["contentHierarchyItemId"] == _contentHierarchyItemId.ToString() &&
                    request.Parameters!["ignoreFailedTaint"] == "True"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executionId);

        // Act
        var outcome = await _subject.GrabInteractiveAsync(
            _contentHierarchyItemId, new Release { Title = "Pilot", Protocol = "torrent" }, _userId)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(InteractiveReleaseGrabResult.Accepted));
            Assert.That(outcome.ExecutionId, Is.EqualTo(executionId));
        }
    }

    [Test]
    public async Task GrabInteractiveAsync_ObtainsTheContentItemThroughTheContentService_WhenTheItemHasNone()
    {
        // Arrange
        _hierarchyItem.EffectiveQualityTierListId = Guid.NewGuid();
        _hierarchyItem.ContentItem = null;
        _downloadClients
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new DownloadClient { Id = Guid.NewGuid(), Name = "Client", ProviderType = "test", Enabled = true }
            ]);
        _releaseRepository
            .Setup(r => r.UpdateAsync(It.IsAny<Release>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _subject.GrabInteractiveAsync(
            _contentHierarchyItemId, new Release { Title = "Pilot", Protocol = "torrent" }, _userId)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _contentService.Verify(
                s => s.GetOrCreateContentItemAsync(
                    _contentHierarchyItemId,
                    It.IsAny<DateTime>(),
                    _userId,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentRepository.Verify(
                r => r.CreateAsync(It.IsAny<ContentItem>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
