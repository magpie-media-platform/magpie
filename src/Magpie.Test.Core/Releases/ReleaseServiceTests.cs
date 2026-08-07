using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Releases;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Integrations;
using Magpie.Models.Paging;
using Magpie.Models.Releases;
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
public sealed class ReleaseServiceTests
{
    private Mock<IReleaseRepository> _acquisitionReleaseRepository = null!;
    private Mock<IReleaseScopeRepository> _acquisitionReleaseScopes = null!;
    private Mock<IDownloadScopeRepository> _acquisitionDownloadScopes = null!;
    private Mock<IDownloadRepository> _acquisitionDownloadRepository = null!;
    private Mock<IDownloadClientRepository> _acquisitionDownloadClients = null!;
    private Mock<IDownloadService> _acquisitionDownloadService = null!;
    private Mock<IContentHierarchyItemRepository> _acquisitionHierarchyItems = null!;
    private Mock<IRootFolderService> _acquisitionRootFolders = null!;
    private IReleaseService _acquisitionSubject = null!;
    private Guid _acquisitionReleaseId;
    private Guid _acquisitionLeafId;
    private Guid _acquisitionShowId;
    private Guid _acquisitionSeasonOneId;
    private Guid _acquisitionSeasonTwoId;
    private Guid _acquisitionEpisodeTwoId;
    private Guid _acquisitionEpisodeThreeId;
    private Guid _acquisitionOrphanId;
    private Guid _acquisitionClientOneId;
    private Guid _acquisitionClientTwoId;
    private Guid _acquisitionQualityTierListId;
    private Guid _acquisitionRootFolderId;

    private Mock<IContentHierarchyItemRepository> _packSelectionContentHierarchyItems = null!;
    private Mock<IReleaseRepository> _packSelectionReleaseRepository = null!;
    private Mock<IReleaseScopeRepository> _packSelectionReleaseScopes = null!;
    private Mock<IDownloadRepository> _packSelectionDownloadRepository = null!;
    private Mock<IQualityTierListRepository> _packSelectionQualityTierLists = null!;
    private Mock<IReleaseMatchContentTypeDefinitionProvider> _packSelectionFactProvider = null!;
    private IReleaseService _packSelectionSubject = null!;
    private Guid _packSelectionEpisodeId;
    private Guid _packSelectionSeasonId;
    private Guid _packSelectionShowId;
    private Guid _packSelectionContentItemId;
    private Guid _packSelectionTierListId;

    private Mock<IContentHierarchyItemRepository> _selectionContentHierarchyItems = null!;
    private Mock<IReleaseRepository> _selectionReleaseRepository = null!;
    private Mock<IReleaseScopeRepository> _selectionReleaseScopes = null!;
    private Mock<IIndexerRepository> _selectionIndexers = null!;
    private Mock<IDownloadRepository> _selectionDownloadRepository = null!;
    private Mock<IQualityTierListRepository> _selectionQualityTierLists = null!;
    private Mock<IContentTypeRegistry> _selectionContentTypeRegistry = null!;
    private Mock<IReleaseMatchContentTypeDefinitionProvider> _selectionFactProvider = null!;
    private Mock<IReleaseComparer> _selectionReleaseComparer = null!;
    private IReleaseService _selectionSubject = null!;
    private ContentItem _selectionCandidate = null!;
    private Guid _selectionHierarchyId;
    private Guid _selectionTierListId;

    private Mock<IContentHierarchyItemRepository> _viewContentHierarchyItems = null!;
    private Mock<IReleaseRepository> _viewReleaseRepository = null!;
    private Mock<IDownloadRepository> _viewDownloadRepository = null!;
    private Mock<IQualityTierListRepository> _viewQualityTierLists = null!;
    private IReleaseService _viewSubject = null!;
    private Guid _viewHierarchyId;

    [SetUp]
    public void SetUp()
    {
        {
            _acquisitionReleaseId = Guid.NewGuid();
            _acquisitionLeafId = Guid.NewGuid();
            _acquisitionShowId = Guid.NewGuid();
            _acquisitionSeasonOneId = Guid.NewGuid();
            _acquisitionSeasonTwoId = Guid.NewGuid();
            _acquisitionEpisodeTwoId = Guid.NewGuid();
            _acquisitionEpisodeThreeId = Guid.NewGuid();
            _acquisitionOrphanId = Guid.NewGuid();
            _acquisitionClientOneId = Guid.NewGuid();
            _acquisitionClientTwoId = Guid.NewGuid();
            _acquisitionQualityTierListId = Guid.NewGuid();
            _acquisitionRootFolderId = Guid.NewGuid();

            _acquisitionReleaseRepository = new Mock<IReleaseRepository>();
            _acquisitionReleaseRepository
                .Setup(r => r.GetByIdAsync(_acquisitionReleaseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Release
                {
                    Id = _acquisitionReleaseId,
                    Protocol = "torrent",
                    ExternalId = "abc",
                    Title = "Show S01E01 1080p",
                    PublishDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });

            _acquisitionReleaseScopes = new Mock<IReleaseScopeRepository>();
            _acquisitionReleaseScopes
                .Setup(r => r.QueryAsync(
                    It.Is<ReleaseScopeQuery>(query => query.ReleaseId == _acquisitionReleaseId),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<ReleaseScope>
                {
                    Items =
                    [
                        new ReleaseScope
                        {
                            Id = Guid.NewGuid(),
                            ReleaseId = _acquisitionReleaseId,
                            ContentHierarchyItemId = _acquisitionLeafId
                        }
                    ],
                    TotalItems = 1,
                });
            _acquisitionDownloadScopes = new Mock<IDownloadScopeRepository>();
            _acquisitionDownloadScopes
                .Setup(r => r.QueryAsync(
                    It.IsAny<DownloadScopeQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<DownloadScope>());
            _acquisitionDownloadScopes
                .Setup(r => r.CreateAsync(It.IsAny<DownloadScope>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DownloadScope scope, Guid? _, CancellationToken _) => scope);

            _acquisitionHierarchyItems = new Mock<IContentHierarchyItemRepository>();
            var tree = new (Guid Id, Guid? ParentId, string Level, string Name)[]
            {
                (_acquisitionShowId, null, "series", "Show"),
                (_acquisitionSeasonOneId, _acquisitionShowId, "season", "Season 1"),
                (_acquisitionSeasonTwoId, _acquisitionShowId, "season", "Season 2"),
                (_acquisitionLeafId, _acquisitionSeasonOneId, "episode", "S1E1"),
                (_acquisitionEpisodeTwoId, _acquisitionSeasonOneId, "episode", "S1E2"),
                (_acquisitionEpisodeThreeId, _acquisitionSeasonTwoId, "episode", "S2E1"),
                (_acquisitionOrphanId, null, "episode", "Orphan"),
            };
            var nodes = new List<ContentHierarchyItem>();
            foreach (var (id, parentId, level, name) in tree)
            {
                var node = new ContentHierarchyItem
                {
                    Id = id,
                    ParentId = parentId,
                    MetadataContainerId = Guid.NewGuid(),
                    ContentType = "tv",
                    Level = level,
                    Slug = name,
                    Name = name,
                    EffectiveMonitoring = true,
                    EffectiveQualityTierListId = _acquisitionQualityTierListId,
                };
                nodes.Add(node);
                _acquisitionHierarchyItems
                    .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(node);
            }

            _acquisitionHierarchyItems
                .Setup(r => r.QueryAsync(
                    It.IsAny<ContentHierarchyItemCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = nodes, TotalItems = nodes.Count, });

            _acquisitionDownloadRepository = new Mock<IDownloadRepository>();
            _acquisitionDownloadRepository
                .Setup(r => r.QueryAsync(
                    It.IsAny<DownloadQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<Download>());
            _acquisitionDownloadRepository
                .Setup(r => r.CreateAsync(It.IsAny<Download>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Download download, Guid? _, CancellationToken _) =>
                {
                    download.Id = Guid.NewGuid();
                    return download;
                });
            _acquisitionDownloadRepository
                .Setup(r => r.UpdateAsync(It.IsAny<Download>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var integration = new Mock<IDownloadClientIntegration>();
            integration.SetupGet(i => i.ProviderType).Returns("downloadclient-a");
            integration.SetupGet(i => i.SupportedProtocols).Returns(["torrent"]);

            _acquisitionDownloadClients = new Mock<IDownloadClientRepository>();
            _acquisitionDownloadClients
                .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DownloadClient
                    {
                        Id = _acquisitionClientOneId,
                        ProviderType = "downloadclient-a",
                        Name = "primary",
                        Enabled = true,
                        Priority = 1,
                        Status = IntegrationHealthStatus.Healthy
                    },
                    new DownloadClient
                    {
                        Id = _acquisitionClientTwoId,
                        ProviderType = "downloadclient-a",
                        Name = "secondary",
                        Enabled = true,
                        Priority = 2,
                        Status = IntegrationHealthStatus.Healthy
                    }
                ]);

            _acquisitionDownloadService = new Mock<IDownloadService>();
            _acquisitionRootFolders = new Mock<IRootFolderService>();

            var transactionManager = new Mock<ITransactionManager>();
            transactionManager
                .Setup(t => t.ExecuteWithTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                    operation(cancellationToken));
            transactionManager
                .Setup(t => t.ExecuteWithTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Download>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<CancellationToken, Task<Download>> operation, CancellationToken cancellationToken) =>
                    operation(cancellationToken));
            var factProvider = new Mock<IReleaseMatchContentTypeDefinitionProvider>();
            factProvider
                .Setup(p => p.Definition)
                .Returns(new ContentTypeDefinition
                {
                    Slug = "tv",
                    DisplayName = "TV",
                    LevelDefinitions =
                    [
                        new ContentHierarchyLevelDefinition
                        {
                            Slug = "series",
                            ParentSlug = null,
                            DisplayName = "Series",
                            CustomMetadataFields = [],
                            OrdinalKind = OrdinalKind.Name,
                        },
                        new ContentHierarchyLevelDefinition
                        {
                            Slug = "season",
                            ParentSlug = "series",
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
            var contentTypeRegistry = new Mock<IContentTypeRegistry>();
            contentTypeRegistry
                .Setup(r => r.FindProviderFor("tv"))
                .Returns(factProvider.Object);
            contentTypeRegistry
                .Setup(r => r.GetDefinitionFor("tv"))
                .Returns(factProvider.Object.Definition);

            var materializedMetadataRepository = new Mock<IMaterializedMetadataRepository>();
            materializedMetadataRepository
                .Setup(repository => repository.QueryAsync(
                    It.IsAny<MetadataCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<MaterializedMetadata>());
            var releaseEvaluationService = new ReleaseEvaluationService(
                _acquisitionHierarchyItems.Object,
                materializedMetadataRepository.Object,
                _acquisitionReleaseRepository.Object,
                _acquisitionDownloadRepository.Object,
                Mock.Of<IQualityTierListRepository>(),
                contentTypeRegistry.Object,
                [],
                TimeProvider.System,
                NullLogger<ReleaseEvaluationService>.Instance);
            _acquisitionSubject = new ReleaseService(
                _acquisitionHierarchyItems.Object,
                Mock.Of<IIndexerRepository>(),
                _acquisitionReleaseRepository.Object,
                _acquisitionReleaseScopes.Object,
                _acquisitionDownloadScopes.Object,
                _acquisitionDownloadRepository.Object,
                _acquisitionDownloadClients.Object,
                [integration.Object],
                _acquisitionDownloadService.Object,
                _acquisitionRootFolders.Object,
                Mock.Of<IAuditRecordRepository>(),
                Mock.Of<IJobManager>(),
                transactionManager.Object,
                Mock.Of<IEventBus>(),
                releaseEvaluationService,
                [],
                TimeProvider.System,
                NullLogger<ReleaseService>.Instance);
        }

        {
            _packSelectionEpisodeId = Guid.NewGuid();
            _packSelectionSeasonId = Guid.NewGuid();
            _packSelectionShowId = Guid.NewGuid();
            _packSelectionContentItemId = Guid.NewGuid();
            _packSelectionTierListId = Guid.NewGuid();
            var createdAt = DateTime.UtcNow;

            var episodeContainer = new MetadataContainer { CreatedAt = createdAt };
            var episodeContribution = new MetadataContribution { ProviderType = "test", Ordinal = 0 };
            episodeContribution.ExternalIds.Add(new ExternalId
            {
                Source = "provider-h",
                Value = "tt5000005",
                Ordinal = 0
            });
            episodeContainer.Contributions.Add(episodeContribution);
            var episode = new ContentHierarchyItem
            {
                Id = _packSelectionEpisodeId,
                ParentId = _packSelectionSeasonId,
                ContentType = "tv",
                Level = "episode",
                Slug = "pilot",
                Name = "Pilot",
                Ordinal = 5,
                EffectiveQualityTierListId = _packSelectionTierListId,
                CreatedAt = createdAt,
                MetadataContainer = episodeContainer,
            };

            _packSelectionContentHierarchyItems = new Mock<IContentHierarchyItemRepository>();
            _packSelectionContentHierarchyItems
                .Setup(r => r.GetByIdAsync(_packSelectionEpisodeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(episode);
            var lineage = new[]
            {
                episode,
                new ContentHierarchyItem
                {
                    Id = _packSelectionSeasonId,
                    ParentId = _packSelectionShowId,
                    ContentType = "tv",
                    Level = "season",
                    Slug = "s1",
                    Name = "Season 1",
                    Ordinal = 1,
                    CreatedAt = createdAt,
                    MetadataContainer = new MetadataContainer { CreatedAt = createdAt },
                },
                new ContentHierarchyItem
                {
                    Id = _packSelectionShowId,
                    ContentType = "tv",
                    Level = "show",
                    Slug = "show",
                    Name = "Show",
                    CreatedAt = createdAt,
                    MetadataContainer = new MetadataContainer { CreatedAt = createdAt },
                },
            };
            _packSelectionContentHierarchyItems
                .Setup(r => r.GetLineageAsync(_packSelectionEpisodeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lineage);
            _packSelectionContentHierarchyItems
                .Setup(r => r.GetLineageAsync(_packSelectionEpisodeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lineage);
            _packSelectionContentHierarchyItems
                .Setup(r => r.QueryAsync(
                    It.IsAny<ContentHierarchyItemCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<ContentHierarchyItem>
                {
                    Items =
                    [
                        new ContentHierarchyItem
                        {
                            Id = _packSelectionEpisodeId,
                            ParentId = _packSelectionSeasonId,
                            ContentType = "tv",
                            Level = "episode",
                            Slug = "pilot",
                            Name = "Pilot",
                            Ordinal = 5,
                            EffectiveQualityTierListId = _packSelectionTierListId,
                            CreatedAt = createdAt,
                            MetadataContainer = new MetadataContainer { CreatedAt = createdAt },
                            ContentItem = new ContentItem
                            {
                                Id = _packSelectionContentItemId,
                                ContentHierarchyItemId = _packSelectionEpisodeId,
                                Fulfillment = FulfillmentStatus.Missing,
                            },
                        }
                    ],
                    TotalItems = 1,
                });

            _packSelectionReleaseRepository = new Mock<IReleaseRepository>();
            _packSelectionReleaseRepository
                .Setup(r => r.QueryAsync(
                    It.IsAny<ReleaseQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<Release>());

            _packSelectionReleaseScopes = new Mock<IReleaseScopeRepository>();
            _packSelectionReleaseScopes
                .Setup(r => r.QueryAsync(
                    It.IsAny<ReleaseScopeQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<ReleaseScope>());

            _packSelectionDownloadRepository = new Mock<IDownloadRepository>();
            _packSelectionDownloadRepository
                .Setup(r => r.QueryAsync(
                    It.IsAny<DownloadQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<Download>());
            _packSelectionDownloadRepository
                .Setup(r => r.ListFailedByReleaseIdentitiesAsync(
                    It.IsAny<IReadOnlyCollection<ReleaseIdentityKey>>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            _packSelectionDownloadRepository
                .Setup(r => r.ListImportedByContentHierarchyItemAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            _packSelectionQualityTierLists = new Mock<IQualityTierListRepository>();
            var fullHd = new QualityTier
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
                CreatedAt = createdAt
            };
            var hd = new QualityTier
            {
                Id = Guid.NewGuid(),
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
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Value = "720p"
                                }
                            }
                        }
                    ]
                },
                CreatedAt = createdAt
            };
            var tierList = new QualityTierList
            {
                Id = _packSelectionTierListId,
                Name = "List",
                CreatedAt = createdAt,
                QualityTierRanks =
                [
                    new QualityTierListRank
                    {
                        QualityTierListId = _packSelectionTierListId,
                        QualityTierId = fullHd.Id,
                        QualityTier = fullHd,
                        Ordinal = 0,
                        IsCutoff = true,
                        CreatedAt = createdAt
                    },
                    new QualityTierListRank
                    {
                        QualityTierListId = _packSelectionTierListId,
                        QualityTierId = hd.Id,
                        QualityTier = hd,
                        Ordinal = 1,
                        CreatedAt = createdAt
                    }
                ]
            };
            _packSelectionQualityTierLists
                .Setup(r => r.GetByIdAsync(_packSelectionTierListId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(tierList);

            _packSelectionFactProvider = new Mock<IReleaseMatchContentTypeDefinitionProvider>();
            _packSelectionFactProvider
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
            _packSelectionFactProvider
                .Setup(p => p.IsMatch(
                    It.IsAny<ContentTarget>(),
                    It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                    It.IsAny<Release>(),
                    It.IsAny<IReadOnlyList<ContentFact>>()))
                .Returns(ReleaseMatchResult.Confident);
            var contentTypeRegistry = new Mock<IContentTypeRegistry>();
            contentTypeRegistry
                .Setup(registry => registry.FindProviderFor("tv"))
                .Returns(_packSelectionFactProvider.Object);
            contentTypeRegistry
                .Setup(registry => registry.GetDefinitionFor("tv"))
                .Returns(_packSelectionFactProvider.Object.Definition);

            var indexerRepository = new Mock<IIndexerRepository>();
            indexerRepository
                .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            var transactionManager = new Mock<ITransactionManager>();
            transactionManager
                .Setup(t => t.ExecuteWithTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                    operation(cancellationToken));
            transactionManager
                .Setup(t => t.ExecuteWithTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Download>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<CancellationToken, Task<Download>> operation, CancellationToken cancellationToken) =>
                    operation(cancellationToken));
            var materializedMetadataRepository = new Mock<IMaterializedMetadataRepository>();
            materializedMetadataRepository
                .Setup(repository => repository.QueryAsync(
                    It.IsAny<MetadataCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<MaterializedMetadata>());
            var releaseEvaluationService = new ReleaseEvaluationService(
                _packSelectionContentHierarchyItems.Object,
                materializedMetadataRepository.Object,
                _packSelectionReleaseRepository.Object,
                _packSelectionDownloadRepository.Object,
                _packSelectionQualityTierLists.Object,
                contentTypeRegistry.Object,
                [],
                TimeProvider.System,
                NullLogger<ReleaseEvaluationService>.Instance);
            _packSelectionSubject = new ReleaseService(
                _packSelectionContentHierarchyItems.Object,
                indexerRepository.Object,
                _packSelectionReleaseRepository.Object,
                _packSelectionReleaseScopes.Object,
                Mock.Of<IDownloadScopeRepository>(),
                _packSelectionDownloadRepository.Object,
                Mock.Of<IDownloadClientRepository>(),
                [],
                Mock.Of<IDownloadService>(),
                Mock.Of<IRootFolderService>(),
                Mock.Of<IAuditRecordRepository>(),
                Mock.Of<IJobManager>(),
                transactionManager.Object,
                Mock.Of<IEventBus>(),
                releaseEvaluationService,
                [],
                TimeProvider.System,
                NullLogger<ReleaseService>.Instance);
        }

        {
            _selectionHierarchyId = Guid.NewGuid();
            _selectionTierListId = Guid.NewGuid();
            _selectionCandidate = new ContentItem
            {
                Id = Guid.NewGuid(),
                ContentHierarchyItemId = _selectionHierarchyId,
                Fulfillment = FulfillmentStatus.Missing
            };

            _selectionContentHierarchyItems = new Mock<IContentHierarchyItemRepository>();
            _selectionContentHierarchyItems
                .Setup(r => r.GetByIdAsync(_selectionHierarchyId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ContentHierarchyItem
                {
                    Id = _selectionHierarchyId,
                    ContentType = "movie",
                    Level = "movie",
                    Slug = "dune",
                    Name = "Dune",
                    EffectiveQualityTierListId = _selectionTierListId,
                    CreatedAt = DateTime.UtcNow,
                });
            var filmContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            var filmContribution = new MetadataContribution { ProviderType = "test", Ordinal = 0 };
            filmContribution.ExternalIds.Add(new ExternalId
            {
                Source = "provider-h",
                Value = "tt1160419",
                Ordinal = 0
            });
            filmContainer.Contributions.Add(filmContribution);
            var lineage = new[]
            {
                new ContentHierarchyItem
                {
                    Id = _selectionHierarchyId,
                    ContentType = "movie",
                    Level = "film",
                    Slug = "dune",
                    Name = "Dune",
                    CreatedAt = DateTime.UtcNow,
                    MetadataContainer = filmContainer,
                }
            };
            _selectionContentHierarchyItems
                .Setup(r => r.GetLineageAsync(_selectionHierarchyId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lineage);
            _selectionContentHierarchyItems
                .Setup(r => r.GetLineageAsync(_selectionHierarchyId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lineage);
            _selectionContentHierarchyItems
                .Setup(r => r.QueryAsync(
                    It.IsAny<ContentHierarchyItemCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<ContentHierarchyItem>
                {
                    Items =
                    [
                        new ContentHierarchyItem
                        {
                            Id = _selectionHierarchyId,
                            ContentType = "movie",
                            Level = "movie",
                            Slug = "dune",
                            Name = "Dune",
                            CreatedAt = DateTime.UtcNow,
                            ContentItem = new ContentItem
                            {
                                Id = _selectionCandidate.Id,
                                ContentHierarchyItemId = _selectionHierarchyId,
                                Fulfillment = _selectionCandidate.Fulfillment,
                            },
                        }
                    ],
                    TotalItems = 1,
                });
            _selectionReleaseRepository = new Mock<IReleaseRepository>();
            _selectionReleaseRepository
                .Setup(r => r.QueryAsync(
                    It.IsAny<ReleaseQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<Release>());

            _selectionReleaseScopes = new Mock<IReleaseScopeRepository>();
            _selectionReleaseScopes
                .Setup(r => r.QueryAsync(
                    It.IsAny<ReleaseScopeQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<ReleaseScope>());

            _selectionDownloadRepository = new Mock<IDownloadRepository>();
            _selectionDownloadRepository
                .Setup(r => r.QueryAsync(
                    It.IsAny<DownloadQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<Download>());
            _selectionDownloadRepository
                .Setup(r => r.ListFailedByReleaseIdentitiesAsync(
                    It.IsAny<IReadOnlyCollection<ReleaseIdentityKey>>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            _selectionDownloadRepository
                .Setup(r => r.ListImportedByContentHierarchyItemAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            _selectionQualityTierLists = new Mock<IQualityTierListRepository>();
            var createdAt = DateTime.UtcNow;
            var ultraHd = new QualityTier
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
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
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Value = "2160p"
                                }
                            }
                        }
                    ]
                },
                CreatedAt = createdAt
            };
            var fullHd = new QualityTier
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
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
                CreatedAt = createdAt
            };
            var hd = new QualityTier
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
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
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Value = "720p"
                                }
                            }
                        }
                    ]
                },
                CreatedAt = createdAt
            };
            var sd = new QualityTier
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Name = "480p",
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
                                    Value = "480p"
                                }
                            }
                        }
                    ]
                },
                CreatedAt = createdAt
            };
            var tierList = new QualityTierList
            {
                Id = _selectionTierListId,
                Name = "List",
                CreatedAt = createdAt,
                QualityTierRanks =
                [
                    new QualityTierListRank
                    {
                        QualityTierListId = _selectionTierListId,
                        QualityTierId = ultraHd.Id,
                        QualityTier = ultraHd,
                        Ordinal = 0,
                        CreatedAt = createdAt
                    },
                    new QualityTierListRank
                    {
                        QualityTierListId = _selectionTierListId,
                        QualityTierId = fullHd.Id,
                        QualityTier = fullHd,
                        Ordinal = 1,
                        IsCutoff = true,
                        CreatedAt = createdAt
                    },
                    new QualityTierListRank
                    {
                        QualityTierListId = _selectionTierListId,
                        QualityTierId = hd.Id,
                        QualityTier = hd,
                        Ordinal = 2,
                        CreatedAt = createdAt
                    },
                    new QualityTierListRank
                    {
                        QualityTierListId = _selectionTierListId,
                        QualityTierId = sd.Id,
                        QualityTier = sd,
                        Ordinal = 3,
                        IsCutoff = true,
                        CreatedAt = createdAt
                    }
                ]
            };
            _selectionQualityTierLists
                .Setup(r => r.GetByIdAsync(_selectionTierListId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(tierList);

            _selectionFactProvider = new Mock<IReleaseMatchContentTypeDefinitionProvider>();
            _selectionFactProvider
                .SetupGet(provider => provider.Definition)
                .Returns(new ContentTypeDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    LevelDefinitions =
                    [
                        new ContentHierarchyLevelDefinition
                        {
                            Slug = "film",
                            ParentSlug = null,
                            DisplayName = "Film",
                            CustomMetadataFields = [],
                            OrdinalKind = OrdinalKind.None,
                        },
                    ],
                });
            _selectionFactProvider
                .Setup(provider => provider.IsMatch(
                    It.IsAny<ContentTarget>(),
                    It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                    It.IsAny<Release>(),
                    It.IsAny<IReadOnlyList<ContentFact>>()))
                .Returns(ReleaseMatchResult.Confident);
            _selectionContentTypeRegistry = new Mock<IContentTypeRegistry>();
            _selectionContentTypeRegistry
                .Setup(registry => registry.FindProviderFor("movie"))
                .Returns(_selectionFactProvider.Object);
            _selectionContentTypeRegistry
                .Setup(registry => registry.GetDefinitionFor("movie"))
                .Returns(_selectionFactProvider.Object.Definition);

            _selectionIndexers = new Mock<IIndexerRepository>();
            _selectionIndexers
                .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            _selectionReleaseComparer = new Mock<IReleaseComparer>();
            _selectionReleaseComparer
                .Setup(comparer => comparer.CanCompare(It.IsAny<Release>()))
                .Returns(true);

            var transactionManager = new Mock<ITransactionManager>();
            transactionManager
                .Setup(t => t.ExecuteWithTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                    operation(cancellationToken));
            transactionManager
                .Setup(t => t.ExecuteWithTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Download>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<CancellationToken, Task<Download>> operation, CancellationToken cancellationToken) =>
                    operation(cancellationToken));
            var materializedMetadataRepository = new Mock<IMaterializedMetadataRepository>();
            materializedMetadataRepository
                .Setup(repository => repository.QueryAsync(
                    It.IsAny<MetadataCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<MaterializedMetadata>());
            var releaseEvaluationService = new ReleaseEvaluationService(
                _selectionContentHierarchyItems.Object,
                materializedMetadataRepository.Object,
                _selectionReleaseRepository.Object,
                _selectionDownloadRepository.Object,
                _selectionQualityTierLists.Object,
                _selectionContentTypeRegistry.Object,
                [],
                TimeProvider.System,
                NullLogger<ReleaseEvaluationService>.Instance);
            _selectionSubject = new ReleaseService(
                _selectionContentHierarchyItems.Object,
                _selectionIndexers.Object,
                _selectionReleaseRepository.Object,
                _selectionReleaseScopes.Object,
                Mock.Of<IDownloadScopeRepository>(),
                _selectionDownloadRepository.Object,
                Mock.Of<IDownloadClientRepository>(),
                [],
                Mock.Of<IDownloadService>(),
                Mock.Of<IRootFolderService>(),
                Mock.Of<IAuditRecordRepository>(),
                Mock.Of<IJobManager>(),
                transactionManager.Object,
                Mock.Of<IEventBus>(),
                releaseEvaluationService,
                [_selectionReleaseComparer.Object],
                TimeProvider.System,
                NullLogger<ReleaseService>.Instance);
        }

        {
            _viewHierarchyId = Guid.NewGuid();
            var hierarchyItem = new ContentHierarchyItem
            {
                Id = _viewHierarchyId,
                ContentType = "movie",
                Level = "movie",
                Slug = "example",
                Name = "Example",
                CreatedAt = DateTime.UtcNow,
            };

            _viewContentHierarchyItems = new Mock<IContentHierarchyItemRepository>();
            _viewContentHierarchyItems
                .Setup(r => r.GetByIdAsync(_viewHierarchyId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(hierarchyItem);
            _viewContentHierarchyItems
                .Setup(r => r.QueryAsync(
                    It.IsAny<ContentHierarchyItemCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<ContentHierarchyItem>());
            _viewContentHierarchyItems
                .Setup(r => r.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([hierarchyItem]);
            _viewContentHierarchyItems
                .Setup(r => r.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([hierarchyItem]);

            _viewReleaseRepository = new Mock<IReleaseRepository>();
            _viewReleaseRepository
                .Setup(r => r.QueryAsync(
                    It.IsAny<ReleaseQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<Release>());

            _viewDownloadRepository = new Mock<IDownloadRepository>();
            _viewDownloadRepository
                .Setup(r => r.QueryAsync(
                    It.IsAny<DownloadQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<Download>());

            var indexerRepository = new Mock<IIndexerRepository>();
            indexerRepository
                .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            _viewQualityTierLists = new Mock<IQualityTierListRepository>();
            _viewQualityTierLists
                .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QualityTierList?)null);

            var releaseMatchProvider = new Mock<IReleaseMatchContentTypeDefinitionProvider>();
            releaseMatchProvider
                .Setup(provider => provider.IsMatch(
                    It.IsAny<ContentTarget>(),
                    It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                    It.IsAny<Release>(),
                    It.IsAny<IReadOnlyList<ContentFact>>()))
                .Returns(ReleaseMatchResult.Confident);
            var contentTypeRegistry = new Mock<IContentTypeRegistry>();
            contentTypeRegistry
                .Setup(registry => registry.FindProviderFor("movie"))
                .Returns(releaseMatchProvider.Object);

            var transactionManager = new Mock<ITransactionManager>();
            transactionManager
                .Setup(t => t.ExecuteWithTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                    operation(cancellationToken));
            transactionManager
                .Setup(t => t.ExecuteWithTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Download>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<CancellationToken, Task<Download>> operation, CancellationToken cancellationToken) =>
                    operation(cancellationToken));

            var materializedMetadataRepository = new Mock<IMaterializedMetadataRepository>();
            materializedMetadataRepository
                .Setup(repository => repository.QueryAsync(
                    It.IsAny<MetadataCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<MaterializedMetadata>());
            var releaseEvaluationService = new ReleaseEvaluationService(
                _viewContentHierarchyItems.Object,
                materializedMetadataRepository.Object,
                _viewReleaseRepository.Object,
                _viewDownloadRepository.Object,
                _viewQualityTierLists.Object,
                contentTypeRegistry.Object,
                [],
                TimeProvider.System,
                NullLogger<ReleaseEvaluationService>.Instance);

            _viewSubject = new ReleaseService(
                _viewContentHierarchyItems.Object,
                indexerRepository.Object,
                _viewReleaseRepository.Object,
                Mock.Of<IReleaseScopeRepository>(),
                Mock.Of<IDownloadScopeRepository>(),
                _viewDownloadRepository.Object,
                Mock.Of<IDownloadClientRepository>(),
                [],
                Mock.Of<IDownloadService>(),
                Mock.Of<IRootFolderService>(),
                Mock.Of<IAuditRecordRepository>(),
                Mock.Of<IJobManager>(),
                transactionManager.Object,
                Mock.Of<IEventBus>(),
                releaseEvaluationService,
                [],
                TimeProvider.System,
                NullLogger<ReleaseService>.Instance);
        }
    }

    [Test]
    public void AcquireAsync_ThrowsMagpieException_WhenQualityDefaultIsUnresolved()
    {
        // Arrange
        _acquisitionRootFolders
            .Setup(service => service.GetAsync(_acquisitionRootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = _acquisitionRootFolderId,
                Name = "TV Library",
                Path = "/tv",
                ContentType = "tv",
            });
        _acquisitionHierarchyItems
            .Setup(repository => repository.GetByIdAsync(_acquisitionLeafId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = _acquisitionLeafId,
                ContentType = "tv",
                Level = "episode",
                Slug = "S1E1",
                Name = "S1E1",
                EffectiveRootFolderId = _acquisitionRootFolderId,
                EffectiveQualityTierListId = null,
            });

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(() =>
            _acquisitionSubject.AcquireAsync(_acquisitionReleaseId, userId: null));

        // Assert
        Assert.That(
            exception!.Message,
            Is.EqualTo(
                "Root folder 'TV Library' has no quality tier list configured for level 'episode'. " +
                "Configure a default quality tier list for this level in the root folder settings."));
        _acquisitionDownloadService.Verify(
            service => service.GrabAsync(
                It.IsAny<Release>(),
                It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task AcquireAsync_ReportsUnmeasuredPhasesAndCompletion_WhenAcquisitionSucceeds()
    {
        // Arrange
        _acquisitionDownloadService
            .Setup(service => service.GrabAsync(
                It.IsAny<Release>(),
                It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrabResult.FromSuccess(new DownloadInfo
            {
                ExternalId = "download-1",
                Status = DownloadStatus.Downloading
            }));
        var progress = new Mock<IProgressReporter>();

        // Act
        await _acquisitionSubject
            .AcquireAsync(_acquisitionReleaseId, userId: null, progress: progress.Object)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            progress.Verify(value => value.Report(0, "The acquisition will load the release."), Times.Once);
            progress.Verify(
                value => value.Report(null, "The acquisition will select a download client."),
                Times.Once);
            progress.Verify(
                value => value.Report(null, "The download client will acquire the release."),
                Times.Once);
            progress.Verify(value => value.Report(null, "The acquisition will save the download."), Times.Once);
            progress.Verify(value => value.Report(1, "The acquisition completed."), Times.Once);
        }
    }

    [Test]
    public void AcquireAsync_DoesNotTryAnotherClient_WhenGrabThrowsAfterDispatch()
    {
        // Arrange
        _acquisitionDownloadService
            .Setup(s => s.GrabAsync(
                It.IsAny<Release>(),
                It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("client dispatched then failed"));

        // Act / Assert
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _acquisitionSubject.AcquireAsync(_acquisitionReleaseId, userId: null));
        _acquisitionDownloadService.Verify(
            s => s.GrabAsync(
                It.IsAny<Release>(),
                It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void AcquireAsync_RecordsAnAmbiguousDispatchFailure_WhenGrabThrowsAfterDispatch()
    {
        // Arrange
        _acquisitionDownloadService
            .Setup(s => s.GrabAsync(
                It.IsAny<Release>(),
                It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("client dispatched then failed"));

        // Act / Assert
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _acquisitionSubject.AcquireAsync(_acquisitionReleaseId, userId: null));
        _acquisitionDownloadRepository.Verify(
            r => r.CreateAsync(
                It.Is<Download>(d => d.Status == DownloadStatus.Failed &&
                    d.FailureReason == DownloadFailureReason.AmbiguousDispatchFailure),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task AcquireAsync_RecordsSuccessfulSource_WhenFirstRetainedSourceIsRejected()
    {
        // Arrange
        var firstSourceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondSourceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var release = new Release
        {
            Id = _acquisitionReleaseId,
            Protocol = "torrent",
            Title = "Show S01E01 1080p",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Sources =
            {
                new ReleaseSource
                {
                    Id = firstSourceId,
                    ReleaseId = _acquisitionReleaseId,
                    Key = "first",
                    IndexerId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Protocol = "torrent",
                    ExternalId = "first",
                    Title = "Show S01E01 1080p",
                    InfoUrl = new Uri("https://first.test/release.torrent"),
                },
                new ReleaseSource
                {
                    Id = secondSourceId,
                    ReleaseId = _acquisitionReleaseId,
                    Key = "second",
                    IndexerId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    Protocol = "torrent",
                    ExternalId = "second",
                    Title = "Show S01E01 1080p",
                    InfoUrl = new Uri("https://second.test/release.torrent"),
                },
            },
        };
        _acquisitionReleaseRepository
            .Setup(r => r.GetByIdAsync(_acquisitionReleaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);
        _acquisitionDownloadClients
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new DownloadClient
                {
                    Id = _acquisitionClientOneId,
                    ProviderType = "downloadclient-a",
                    Name = "primary",
                    Enabled = true,
                    Priority = 1,
                    Status = IntegrationHealthStatus.Healthy
                }
            ]);
        _acquisitionDownloadService
            .SetupSequence(s => s.GrabAsync(
                It.IsAny<Release>(),
                It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrabResult { ErrorMessage = "first source rejected" })
            .ReturnsAsync(GrabResult.FromSuccess(new DownloadInfo
            {
                ExternalId = "download-1",
                Status = DownloadStatus.Downloading
            }));

        // Act
        var result = await _acquisitionSubject.AcquireAsync(_acquisitionReleaseId, userId: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(Download.ReleaseSourceId)).EqualTo(secondSourceId));
            _acquisitionDownloadService.Verify(
                service => service.GrabAsync(
                    It.Is<Release>(candidate => candidate.ExternalId == "first"),
                    It.IsAny<DownloadClient>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<string?>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _acquisitionDownloadService.Verify(
                service => service.GrabAsync(
                    It.Is<Release>(candidate => candidate.ExternalId == "second"),
                    It.IsAny<DownloadClient>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<string?>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task AcquireAsync_ScopesTheDownloadToTheWholeCoverage_WhenGrabbingAPackForOneEpisode()
    {
        // Arrange
        _acquisitionReleaseScopes
            .Setup(r => r.QueryAsync(
                It.Is<ReleaseScopeQuery>(query => query.ReleaseId == _acquisitionReleaseId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ReleaseScope>
            {
                Items =
                [
                    new ReleaseScope
                    {
                        Id = Guid.NewGuid(),
                        ReleaseId = _acquisitionReleaseId,
                        ContentHierarchyItemId = _acquisitionSeasonOneId
                    },
                    new ReleaseScope
                    {
                        Id = Guid.NewGuid(),
                        ReleaseId = _acquisitionReleaseId,
                        ContentHierarchyItemId = _acquisitionLeafId
                    },
                    new ReleaseScope
                    {
                        Id = Guid.NewGuid(),
                        ReleaseId = _acquisitionReleaseId,
                        ContentHierarchyItemId = _acquisitionEpisodeTwoId
                    },
                ],
                TotalItems = 3,
            });
        _acquisitionDownloadService
            .Setup(s => s.GrabAsync(
                It.IsAny<Release>(),
                It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrabResult.FromSuccess(new DownloadInfo
            {
                ExternalId = "ext-1",
                Status = DownloadStatus.Downloading
            }));

        // Act
        await _acquisitionSubject
            .AcquireAsync(_acquisitionReleaseId, userId: null, grabTargetContentHierarchyItemId: _acquisitionLeafId)
            .ConfigureAwait(false);

        // Assert
        _acquisitionDownloadScopes.Verify(
            r => r.CreateAsync(
                It.Is<DownloadScope>(scope =>
                    scope.ContentHierarchyItemId == _acquisitionSeasonOneId ||
                    scope.ContentHierarchyItemId == _acquisitionLeafId ||
                    scope.ContentHierarchyItemId == _acquisitionEpisodeTwoId),
                null,
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Test]
    public async Task AcquireAsync_DropsFulfilledLeavesFromTheDownloadScope_WhenGrabbingAPack()
    {
        // Arrange
        // the fulfilled episode is absent from the needs-acquisition query, which filters on fulfillment
        _acquisitionHierarchyItems
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = _acquisitionSeasonOneId,
                        ParentId = _acquisitionShowId,
                        ContentType = "tv",
                        Level = "season",
                        Slug = "Season 1",
                        Name = "Season 1",
                        EffectiveMonitoring = true,
                        EffectiveQualityTierListId = _acquisitionQualityTierListId,
                    },
                    new ContentHierarchyItem
                    {
                        Id = _acquisitionLeafId,
                        ParentId = _acquisitionSeasonOneId,
                        ContentType = "tv",
                        Level = "episode",
                        Slug = "S1E1",
                        Name = "S1E1",
                        EffectiveMonitoring = true,
                        EffectiveQualityTierListId = _acquisitionQualityTierListId,
                    },
                ],
                TotalItems = 2,
            });
        _acquisitionReleaseScopes
            .Setup(r => r.QueryAsync(
                It.Is<ReleaseScopeQuery>(query => query.ReleaseId == _acquisitionReleaseId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ReleaseScope>
            {
                Items =
                [
                    new ReleaseScope
                    {
                        Id = Guid.NewGuid(),
                        ReleaseId = _acquisitionReleaseId,
                        ContentHierarchyItemId = _acquisitionSeasonOneId
                    },
                    new ReleaseScope
                    {
                        Id = Guid.NewGuid(),
                        ReleaseId = _acquisitionReleaseId,
                        ContentHierarchyItemId = _acquisitionLeafId
                    },
                    new ReleaseScope
                    {
                        Id = Guid.NewGuid(),
                        ReleaseId = _acquisitionReleaseId,
                        ContentHierarchyItemId = _acquisitionEpisodeTwoId
                    },
                ],
                TotalItems = 3,
            });
        _acquisitionDownloadService
            .Setup(s => s.GrabAsync(
                It.IsAny<Release>(),
                It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrabResult.FromSuccess(new DownloadInfo
            {
                ExternalId = "ext-1",
                Status = DownloadStatus.Downloading
            }));

        // Act
        await _acquisitionSubject
            .AcquireAsync(_acquisitionReleaseId, userId: null, grabTargetContentHierarchyItemId: _acquisitionLeafId)
            .ConfigureAwait(false);

        // Assert
        _acquisitionDownloadScopes.Verify(
            r => r.CreateAsync(
                It.Is<DownloadScope>(scope =>
                    scope.ContentHierarchyItemId == _acquisitionSeasonOneId ||
                    scope.ContentHierarchyItemId == _acquisitionLeafId),
                null,
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task AcquireAsync_ReusesTheActiveDownload_WhenOneIsAlreadyInFlight()
    {
        // Arrange
        var activeDownload = new Download
        {
            Id = Guid.NewGuid(),
            ReleaseId = _acquisitionReleaseId,
            DownloadClientId = _acquisitionClientOneId,
            ExternalId = "ext-active",
            Title = "Show S01 SEASONPACK 1080p",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        _acquisitionDownloadRepository
            .Setup(r => r.QueryAsync(
                It.Is<DownloadQuery>(q => q.ReleaseId == _acquisitionReleaseId && q.DownloadClientId == null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download> { Items = [activeDownload], TotalItems = 1 });

        // Act
        var result = await _acquisitionSubject.AcquireAsync(_acquisitionReleaseId, userId: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(Download.Id)).EqualTo(activeDownload.Id));
            _acquisitionDownloadService.Verify(
                s => s.GrabAsync(
                    It.IsAny<Release>(),
                    It.IsAny<DownloadClient>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<string?>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SelectBestReleaseAsync_GrabsLinkedPack_WhenNoAcceptableSingleExists()
    {
        // Arrange
        var seasonPack = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Pilot S01 SEASONPACK 1080p WEB",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        _packSelectionReleaseRepository
            .Setup(r => r.QueryAsync(
                It.Is<ReleaseQuery>(q => q.ContentHierarchyItemId == _packSelectionEpisodeId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [seasonPack], TotalItems = 1 });
        _packSelectionReleaseScopes
            .Setup(r => r.QueryAsync(
                It.Is<ReleaseScopeQuery>(query => query.ReleaseIds != null && query.ReleaseIds.Contains(seasonPack.Id)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ReleaseScope>
            {
                Items =
                [
                    new ReleaseScope { Id = Guid.NewGuid(), ReleaseId = seasonPack.Id },
                    new ReleaseScope { Id = Guid.NewGuid(), ReleaseId = seasonPack.Id },
                    new ReleaseScope { Id = Guid.NewGuid(), ReleaseId = seasonPack.Id },
                ],
                TotalItems = 3,
            });

        // Act
        var selected = await _packSelectionSubject
            .SelectBestReleaseAsync(_packSelectionContentItemId, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(selected, Has.Property(nameof(Release.Id)).EqualTo(seasonPack.Id));
    }

    [TestCase("I'm Ready for a Close-Up Cock", "Im Ready For A Close Up Cock")]
    [TestCase("Close-Up Cock", "Close Up Cock")]
    public async Task GetReleaseViewAsync_MatchesTitle_WhenApostropheOrHyphenDiffers(
        string targetTitle,
        string releaseTitle)
    {
        // Arrange
        var lineage = new[]
        {
            new ContentHierarchyItem
            {
                Id = _selectionHierarchyId,
                ContentType = "movie",
                Level = "film",
                Slug = "title",
                Name = targetTitle,
                CreatedAt = DateTime.UtcNow,
                MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            }
        };
        _selectionContentHierarchyItems
            .Setup(r => r.GetLineageAsync(_selectionHierarchyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lineage);
        _selectionContentHierarchyItems
            .Setup(r => r.GetLineageAsync(_selectionHierarchyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lineage);
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = releaseTitle,
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        _selectionReleaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [release], TotalItems = 1 });

        // Act
        var view = await _selectionSubject.GetReleaseViewAsync(_selectionHierarchyId).ConfigureAwait(false);

        // Assert
        Assert.That(view!.Single(row => row.Id == release.Id).IsMatch, Is.True);
    }

    [Test]
    public async Task GetReleaseViewAsync_ReportsEligible_WhenTitleMatches()
    {
        // Arrange
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.1080p.BluRay.x264-GRP",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        _selectionReleaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [release], TotalItems = 1 });

        // Act
        var view = await _selectionSubject.GetReleaseViewAsync(_selectionHierarchyId).ConfigureAwait(false);

        // Assert
        var row = view!.Single(candidate => candidate.Id == release.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.IsMatch, Is.True);
            Assert.That(row.DecisionReason, Is.EqualTo("Eligible but not yet grabbed"));
        }
    }

    [Test]
    public async Task SelectBestReleaseAsync_ReturnsNull_WhenNoStoredReleasesExist()
    {
        // Arrange

        // Act
        var selected = await _selectionSubject
            .SelectBestReleaseAsync(_selectionCandidate.Id, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(selected, Is.Null);
    }

    [Test]
    public async Task SelectBestReleaseAsync_SelectsBestRelease_WhenMatchesHaveOnlyTextSupport()
    {
        // Arrange
        var betterQuality = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.1080p.BluRay.x264-GRP",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        var worseQuality = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.720p.BluRay.x264-GRP",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _selectionReleaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [worseQuality, betterQuality], TotalItems = 2 });

        // Act
        var releaseView = await _selectionSubject.GetReleaseViewAsync(_selectionHierarchyId).ConfigureAwait(false);
        var selected = await _selectionSubject
            .SelectBestReleaseAsync(_selectionCandidate.Id, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(releaseView!.Single(row => row.Id == betterQuality.Id).IsMatch, Is.True);
            Assert.That(selected, Has.Property(nameof(Release.Id)).EqualTo(betterQuality.Id));
        }
    }

    [Test]
    public async Task SelectBestReleaseAsync_UsesReleaseComparer_WhenPrimaryRanksTie()
    {
        // Arrange
        var fewerPeers = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.1080p.BluRay.x264-GRP",
            ExternalId = "court-ordered-smoothie",
            PublishDate = new DateTime(2026, 1, 1),
            CreatedAt = new DateTime(2026, 1, 1),
        };
        var morePeers = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.1080p.BluRay.x264-GRP",
            ExternalId = "lion-tamer",
            PublishDate = new DateTime(2025, 1, 1),
            CreatedAt = new DateTime(2025, 1, 1),
        };
        _selectionReleaseRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [fewerPeers, morePeers], TotalItems = 2, });
        _selectionReleaseComparer
            .Setup(comparer => comparer.Compare(fewerPeers, morePeers))
            .Returns(1);
        _selectionReleaseComparer
            .Setup(comparer => comparer.Compare(morePeers, fewerPeers))
            .Returns(-1);

        // Act
        var selected = await _selectionSubject
            .SelectBestReleaseAsync(_selectionCandidate.Id, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(selected, Has.Property(nameof(Release.ExternalId)).EqualTo("lion-tamer"));
    }

    [Test]
    public async Task SelectBestReleaseAsync_UsesLowestCutoffAsCeiling_WhenNoCurrentQualityExists()
    {
        // Arrange
        var bestQuality = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.2160p.BluRay.x264-GRP",
            ExternalId = "best-quality",
            PublishDate = new DateTime(2026, 1, 1),
            CreatedAt = new DateTime(2026, 1, 1),
        };
        var lowestCutoff = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.480p.BluRay.x264-GRP",
            ExternalId = "lowest-cutoff",
            PublishDate = new DateTime(2025, 1, 1),
            CreatedAt = new DateTime(2025, 1, 1),
        };
        _selectionReleaseRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [bestQuality, lowestCutoff], TotalItems = 2, });

        // Act
        var selected = await _selectionSubject
            .SelectBestReleaseAsync(_selectionCandidate.Id, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(selected, Has.Property(nameof(Release.ExternalId)).EqualTo("lowest-cutoff"));
    }

    [Test]
    public async Task SelectBestReleaseAsync_UsesNearestHigherCutoffAsCeiling_WhenCurrentQualityExists()
    {
        // Arrange
        var importedReleaseId = Guid.NewGuid();
        var bestQuality = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.2160p.BluRay.x264-GRP",
            ExternalId = "best-quality",
            PublishDate = new DateTime(2026, 1, 1),
            CreatedAt = new DateTime(2026, 1, 1),
        };
        var nearestCutoff = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.1080p.BluRay.x264-GRP",
            ExternalId = "nearest-cutoff",
            PublishDate = new DateTime(2025, 1, 1),
            CreatedAt = new DateTime(2025, 1, 1),
        };
        _selectionDownloadRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<DownloadQuery>(query => query.ContentItemId == _selectionHierarchyId),
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
                        DownloadClientId = Guid.NewGuid(),
                        ExternalId = "imported",
                        Title = "Dune.2021.720p.BluRay.x264-GRP",
                        Status = DownloadStatus.Imported,
                        CreatedAt = new DateTime(2025, 1, 1),
                    },
                ],
                TotalItems = 1,
            });
        _selectionReleaseRepository
            .Setup(repository => repository.GetByIdAsync(importedReleaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Release
            {
                Id = importedReleaseId,
                Protocol = "torrent",
                Title = "Dune.2021.720p.BluRay.x264-GRP",
                ExternalId = "imported",
                CreatedAt = new DateTime(2025, 1, 1),
            });
        _selectionReleaseRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [bestQuality, nearestCutoff], TotalItems = 2, });

        // Act
        var selected = await _selectionSubject
            .SelectBestReleaseAsync(_selectionCandidate.Id, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(selected, Has.Property(nameof(Release.ExternalId)).EqualTo("nearest-cutoff"));
    }

    [Test]
    public async Task SelectBestReleaseAsync_ReturnsRelease_WhenReleaseExternalIdMatchesTarget()
    {
        // Arrange
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.1080p.BluRay.x264-GRP",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ExternalIds =
            {
                new ExternalId { Source = "PROVIDER-H", Value = "TT1160419" },
            },
        };
        _selectionReleaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [release], TotalItems = 1 });

        // Act
        var selected = await _selectionSubject
            .SelectBestReleaseAsync(_selectionCandidate.Id, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(selected, Has.Property(nameof(Release.Id)).EqualTo(release.Id));
    }

    [Test]
    public async Task SelectBestReleaseAsync_ReturnsRelease_WhenNoLeafImportSuppressionExists()
    {
        // Arrange
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.1080p.BluRay.x264-GRP",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IdentityKeys =
            {
                new ReleaseIdentityKey { Authority = "provider", Value = "pack-key" },
            },
            ExternalIds =
            {
                new ExternalId { Source = "provider-h", Value = "tt1160419" },
            },
        };
        _selectionReleaseRepository
            .Setup(r => r.QueryAsync(
                It.Is<ReleaseQuery>(q => q.ContentHierarchyItemId == _selectionHierarchyId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [release], TotalItems = 1 });

        // Act
        var selected = await _selectionSubject
            .SelectBestReleaseAsync(_selectionCandidate.Id, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(selected, Has.Property(nameof(Release.Id)).EqualTo(release.Id));
    }

    [Test]
    public async Task SelectBestReleaseAsync_SkipsRelease_WhenLeafImportSuppressionMatchesIdentity()
    {
        // Arrange
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.1080p.BluRay.x264-GRP",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IdentityKeys =
            {
                new ReleaseIdentityKey { Authority = "provider", Value = "pack-key" },
            },
            ExternalIds =
            {
                new ExternalId { Source = "provider-h", Value = "tt1160419" },
            },
        };
        _selectionReleaseRepository
            .Setup(r => r.QueryAsync(
                It.Is<ReleaseQuery>(q => q.ContentHierarchyItemId == _selectionHierarchyId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [release], TotalItems = 1 });
        _selectionDownloadRepository
            .Setup(r => r.ListImportedByContentHierarchyItemAsync(
                _selectionHierarchyId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Download
                {
                    Id = Guid.NewGuid(),
                    ReleaseId = Guid.NewGuid(),
                    DownloadClientId = Guid.NewGuid(),
                    ExternalId = string.Empty,
                    Title = "pack import that skipped this leaf",
                    Status = DownloadStatus.Imported,
                    DateCompleted = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    ReleaseIdentityKeys =
                    {
                        new ReleaseIdentityKey { Authority = "provider", Value = "pack-key" },
                    },
                }
            ]);

        // Act
        var selected = await _selectionSubject
            .SelectBestReleaseAsync(_selectionCandidate.Id, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(selected, Is.Null);
    }

    [TestCase("other", "tt1160419")]
    [TestCase("provider-h", "tt0000000")]
    public async Task SelectBestReleaseAsync_SelectsRelease_WhenExternalIdDiffersAndTitleFactsMatch(
        string source,
        string value)
    {
        // Arrange
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.1080p.BluRay.x264-GRP",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ExternalIds =
            {
                new ExternalId { Source = source, Value = value },
            },
        };
        _selectionReleaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [release], TotalItems = 1 });

        // Act
        var selected = await _selectionSubject
            .SelectBestReleaseAsync(_selectionCandidate.Id, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(selected, Has.Property(nameof(Release.Id)).EqualTo(release.Id));
    }

    [Test]
    public async Task SelectBestReleaseAsync_ReturnsNull_WhenNoReleaseMatchesTarget()
    {
        // Arrange
        var unrelated = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Batman.2021.1080p.BluRay.x264-GRP",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _selectionFactProvider
            .Setup(provider => provider.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.Is<Release>(release => release.Id == unrelated.Id),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(ReleaseMatchResult.NoMatch);
        _selectionReleaseRepository
            .Setup(r => r.QueryAsync(
                It.Is<ReleaseQuery>(q => q.ContentHierarchyItemId == _selectionHierarchyId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [unrelated], TotalItems = 1 });

        // Act
        var selected = await _selectionSubject
            .SelectBestReleaseAsync(_selectionCandidate.Id, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(selected, Is.Null);
    }

    [Test]
    public async Task SelectBestReleaseAsync_ReturnsNull_WhenNoQualityTierMatchesAnyRelease()
    {
        // Arrange
        var unrankable = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.360p.DVDRip.x264-GRP",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _selectionReleaseRepository
            .Setup(r => r.QueryAsync(
                It.Is<ReleaseQuery>(q => q.ContentHierarchyItemId == _selectionHierarchyId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [unrankable], TotalItems = 1 });

        // Act
        var selected = await _selectionSubject
            .SelectBestReleaseAsync(_selectionCandidate.Id, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(selected, Is.Null);
    }

    [Test]
    public async Task SelectBestReleaseAsync_SelectsUntaintedFallback_WhenMatchesHaveOnlyTextSupport()
    {
        // Arrange
        var tainted = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.1080p.BluRay.x264-GRP",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "tainted-key" } }
        };
        var fallback = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.720p.BluRay.x264-GRP",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "clean-key" } }
        };
        _selectionReleaseRepository
            .Setup(r => r.QueryAsync(
                It.Is<ReleaseQuery>(q => q.ContentHierarchyItemId == _selectionHierarchyId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [tainted, fallback], TotalItems = 2 });
        _selectionDownloadRepository
            .Setup(r => r.ListFailedByReleaseIdentitiesAsync(
                It.Is<IReadOnlyCollection<ReleaseIdentityKey>>(keys => keys.Any(key => key.Value == "tainted-key")),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Download
                {
                    Id = Guid.NewGuid(),
                    ReleaseId = tainted.Id,
                    DownloadClientId = Guid.NewGuid(),
                    ExternalId = string.Empty,
                    Title = "failed grab",
                    Status = DownloadStatus.Failed,
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        // Act
        var selected = await _selectionSubject
            .SelectBestReleaseAsync(_selectionCandidate.Id, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(selected, Has.Property(nameof(Release.Id)).EqualTo(fallback.Id));
    }

    [Test]
    public async Task SelectBestReleaseAsync_ReturnsNull_WhenMatchingReleaseAlreadyInFlight()
    {
        // Arrange
        var acceptable = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.1080p.BluRay.x264-GRP",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _selectionReleaseRepository
            .Setup(r => r.QueryAsync(
                It.Is<ReleaseQuery>(q => q.ContentHierarchyItemId == _selectionHierarchyId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [acceptable], TotalItems = 1 });
        _selectionDownloadRepository
            .Setup(r => r.QueryAsync(
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
                        ReleaseId = acceptable.Id,
                        DownloadClientId = Guid.NewGuid(),
                        ExternalId = "ext-1",
                        Title = acceptable.Title,
                        Status = DownloadStatus.Downloading,
                        CreatedAt = DateTime.UtcNow,
                    }
                ],
                TotalItems = 1
            });

        // Act
        var selected = await _selectionSubject
            .SelectBestReleaseAsync(_selectionCandidate.Id, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(selected, Is.Null);
    }

    [TestCase("Dune.2021.1080p.BluRay.x264-GRP", "Dune.2021.1080p.PROPER.BluRay.x264-GRP", true)]
    [TestCase("Dune.2021.1080p.BluRay.x264", "Dune.2021.1080p.PROPER.BluRay.x264", false)]
    public async Task SelectBestReleaseAsync_Result_WhenProperRevisionGroupIsEvaluated(
        string importedTitle,
        string properTitle,
        bool shouldSelect)
    {
        // Arrange
        var upgradable = _selectionCandidate with { Fulfillment = FulfillmentStatus.Upgradable };
        _selectionContentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = _selectionHierarchyId,
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "dune",
                        Name = "Dune",
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = upgradable.Id,
                            ContentHierarchyItemId = _selectionHierarchyId,
                            Fulfillment = upgradable.Fulfillment,
                        },
                    }
                ],
                TotalItems = 1,
            });
        var importedRelease = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = importedTitle,
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var properRevision = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = properTitle,
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _selectionReleaseRepository
            .Setup(r => r.QueryAsync(
                It.Is<ReleaseQuery>(q => q.ContentHierarchyItemId == _selectionHierarchyId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [properRevision], TotalItems = 1 });
        _selectionReleaseRepository
            .Setup(r => r.GetByIdAsync(importedRelease.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(importedRelease);
        _selectionDownloadRepository
            .Setup(r => r.QueryAsync(
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
                        ReleaseId = importedRelease.Id,
                        DownloadClientId = Guid.NewGuid(),
                        ExternalId = "ext-imported",
                        Title = importedRelease.Title,
                        Status = DownloadStatus.Imported,
                        CreatedAt = DateTime.UtcNow
                    }
                ],
                TotalItems = 1
            });

        // Act
        var selected = await _selectionSubject
            .SelectBestReleaseAsync(upgradable.Id, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(selected?.Id, shouldSelect ? Is.EqualTo(properRevision.Id) : Is.Null);
    }

    [Test]
    public async Task SelectBestReleaseAsync_ReturnsNull_WhenCandidateIsNotAnUpgradeOverImportedQuality()
    {
        // Arrange
        var upgradable = _selectionCandidate with { Fulfillment = FulfillmentStatus.Upgradable };
        _selectionContentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = _selectionHierarchyId,
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "dune",
                        Name = "Dune",
                        CreatedAt = DateTime.UtcNow,
                        ContentItem = new ContentItem
                        {
                            Id = upgradable.Id,
                            ContentHierarchyItemId = _selectionHierarchyId,
                            Fulfillment = upgradable.Fulfillment,
                        },
                    }
                ],
                TotalItems = 1,
            });
        var importedRelease = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.1080p.BluRay.x264-GRP",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var sameQuality = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Dune.2021.1080p.WEB-DL.x264-OTHERGRP",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _selectionReleaseRepository
            .Setup(r => r.QueryAsync(
                It.Is<ReleaseQuery>(q => q.ContentHierarchyItemId == _selectionHierarchyId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [sameQuality], TotalItems = 1 });
        _selectionReleaseRepository
            .Setup(r => r.GetByIdAsync(importedRelease.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(importedRelease);
        _selectionDownloadRepository
            .Setup(r => r.QueryAsync(
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
                        ReleaseId = importedRelease.Id,
                        DownloadClientId = Guid.NewGuid(),
                        ExternalId = "ext-imported",
                        Title = importedRelease.Title,
                        Status = DownloadStatus.Imported,
                        CreatedAt = DateTime.UtcNow
                    }
                ],
                TotalItems = 1
            });

        // Act
        var selected = await _selectionSubject
            .SelectBestReleaseAsync(upgradable.Id, userId: null, correlationId: null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(selected, Is.Null);
    }

    [Test]
    public async Task GetReleaseViewAsync_ReturnsNull_WhenContentHierarchyItemNotFound()
    {
        // Arrange
        var missingId = Guid.NewGuid();
        _viewContentHierarchyItems
            .Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentHierarchyItem?)null);

        // Act
        var result = await _viewSubject.GetReleaseViewAsync(missingId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetReleaseViewAsync_ReturnsEmpty_WhenNoReleasesExist()
    {
        // Arrange
        // Act
        var result = await _viewSubject.GetReleaseViewAsync(_viewHierarchyId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetReleaseViewAsync_ReturnsRowPerRelease_WhenReleasesExist()
    {
        // Arrange
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        _viewReleaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release>
            {
                Items =
                [
                    new Release
                    {
                        Id = firstId,
                        Protocol = "torrent",
                        Title = "Release-00",
                        ExternalId = "external-00",
                        Categories = ["Movies/HD"],
                        Languages = ["English"],
                        Properties = """{"seeders":"3"}""",
                        PublishDate = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow,
                    },
                    new Release
                    {
                        Id = secondId,
                        Protocol = "torrent",
                        Title = "Release-01",
                        ExternalId = "external-01",
                        PublishDate = DateTime.UtcNow.AddMinutes(-1),
                        CreatedAt = DateTime.UtcNow,
                    },
                    new Release
                    {
                        Id = thirdId,
                        Protocol = "torrent",
                        Title = "Release-02",
                        ExternalId = "external-02",
                        PublishDate = DateTime.UtcNow.AddMinutes(-2),
                        CreatedAt = DateTime.UtcNow,
                    },
                ],
                TotalItems = 3,
            });

        // Act
        var result = await _viewSubject.GetReleaseViewAsync(_viewHierarchyId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Select(row => row.Id), Is.EquivalentTo([firstId, secondId, thirdId]));
            var first = result.Single(row => row.Id == firstId);
            Assert.That(first.ExternalId, Is.EqualTo("external-00"));
            Assert.That(first.Categories, Is.EqualTo(["Movies/HD"]));
            Assert.That(first.Languages, Is.EqualTo(["English"]));
            Assert.That(first.Properties, Is.EqualTo("""{"seeders":"3"}"""));
        }
    }

    [Test]
    public async Task GetReleaseViewAsync_ReturnsOneRowWithEverySource_WhenLegacyBtihDuplicatesExist()
    {
        // Arrange
        var first = new Release
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            IndexerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Protocol = "torrent",
            Title = "Movie.2024.1080p",
            ExternalId = "source-1",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IdentityKeys =
            {
                new ReleaseIdentityKey
                {
                    Authority = "btih",
                    Value = "ffffffffffffffffffffffffffffffffffffffff",
                },
            },
        };
        var second = new Release
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            IndexerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Protocol = "torrent",
            Title = "Movie.2024.1080p",
            ExternalId = "source-2",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow.AddMinutes(1),
            IdentityKeys =
            {
                new ReleaseIdentityKey
                {
                    Authority = "btih",
                    Value = "ffffffffffffffffffffffffffffffffffffffff",
                },
            },
        };
        var third = new Release
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            IndexerId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Protocol = "torrent",
            Title = "Movie.2024.1080p",
            ExternalId = "source-3",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow.AddMinutes(2),
            IdentityKeys =
            {
                new ReleaseIdentityKey
                {
                    Authority = "btih",
                    Value = "ffffffffffffffffffffffffffffffffffffffff",
                },
            },
        };
        var releases = new[] { first, second, third };

        _viewReleaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = releases, TotalItems = 3 });

        // Act
        var result = await _viewSubject.GetReleaseViewAsync(_viewHierarchyId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetReleaseViewAsync_ReturnsSeparateRows_WhenLegacyFallbackAndImmutableRowsOverlap()
    {
        // Arrange
        var fallback = new Release
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            IndexerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Protocol = "torrent",
            Title = "Movie.2024.1080p",
            ExternalId = "fallback",
            PublishDate = new DateTime(2026, 1, 1),
            Size = 1_000,
            CreatedAt = new DateTime(2026, 1, 1),
            IdentityKeys =
            {
                new ReleaseIdentityKey
                {
                    Authority = "fallback",
                    Value = "f5fe531df03ed3044d8bc63fe1104632ab2fed46abbc08c3a49168db8dfc869a",
                },
            },
        };
        var immutable = new Release
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            IndexerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Protocol = "torrent",
            Title = "Movie.2024.1080p",
            ExternalId = "immutable",
            PublishDate = new DateTime(2026, 1, 2),
            Size = 1_000,
            CreatedAt = new DateTime(2026, 1, 2),
            IdentityKeys =
            {
                new ReleaseIdentityKey
                {
                    Authority = "btih",
                    Value = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                },
            },
        };
        _viewReleaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [fallback, immutable], TotalItems = 2 });

        // Act
        var result = await _viewSubject.GetReleaseViewAsync(_viewHierarchyId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetReleaseViewAsync_ReturnsSeparateRows_WhenSourceIdentityIsAbsent()
    {
        // Arrange
        var first = new Release
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Protocol = "torrent",
            Title = "Movie.2024.1080p",
            PublishDate = new DateTime(2026, 1, 1),
            Size = 1_000,
            CreatedAt = new DateTime(2026, 1, 1),
        };
        var second = new Release
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Protocol = "usenet",
            Title = "Different.Show.2025.720p",
            PublishDate = new DateTime(2026, 1, 2),
            Size = 2_000,
            CreatedAt = new DateTime(2026, 1, 2),
        };
        _viewReleaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [first, second], TotalItems = 2 });

        // Act
        var result = await _viewSubject.GetReleaseViewAsync(_viewHierarchyId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetReleaseViewAsync_ReturnsSeparateRows_WhenImmutableIdentitiesConflict()
    {
        // Arrange
        var indexerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var first = new Release
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            IndexerId = indexerId,
            Protocol = "torrent",
            ExternalId = "shared-source",
            Title = "Movie.2024.1080p",
            PublishDate = new DateTime(2026, 1, 1),
            CreatedAt = new DateTime(2026, 1, 1),
            IdentityKeys =
            {
                new ReleaseIdentityKey
                {
                    Authority = "btih",
                    Value = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                },
            },
        };
        var second = new Release
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            IndexerId = indexerId,
            Protocol = "torrent",
            ExternalId = "shared-source",
            Title = "Movie.2024.1080p",
            PublishDate = new DateTime(2026, 1, 2),
            CreatedAt = new DateTime(2026, 1, 2),
            IdentityKeys =
            {
                new ReleaseIdentityKey
                {
                    Authority = "btih",
                    Value = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                },
            },
        };
        _viewReleaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [first, second], TotalItems = 2 });

        // Act
        var result = await _viewSubject.GetReleaseViewAsync(_viewHierarchyId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetReleaseViewAsync_ReturnsOneRow_WhenHybridIdentityConnectsLegacyRows()
    {
        // Arrange
        var v1 = new Release
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            IndexerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Protocol = "torrent",
            Title = "Movie v1",
            ExternalId = "v1",
            PublishDate = new DateTime(2026, 1, 1),
            CreatedAt = new DateTime(2026, 1, 1),
            IdentityKeys =
            {
                new ReleaseIdentityKey
                {
                    Authority = "btih",
                    Value = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                },
            },
        };
        var v2 = new Release
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            IndexerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Protocol = "torrent",
            Title = "Movie v2",
            ExternalId = "v2",
            PublishDate = new DateTime(2026, 1, 2),
            CreatedAt = new DateTime(2026, 1, 2),
            IdentityKeys =
            {
                new ReleaseIdentityKey
                {
                    Authority = "btmh",
                    Value = "1220bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                },
            },
        };
        var hybrid = new Release
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            IndexerId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Protocol = "torrent",
            Title = "Movie hybrid",
            ExternalId = "hybrid",
            PublishDate = new DateTime(2026, 1, 3),
            CreatedAt = new DateTime(2026, 1, 3),
            IdentityKeys =
            {
                new ReleaseIdentityKey
                {
                    Authority = "btih",
                    Value = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                },
                new ReleaseIdentityKey
                {
                    Authority = "btmh",
                    Value = "1220bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                },
            },
        };
        _viewReleaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [v1, v2, hybrid], TotalItems = 3 });

        // Act
        var result = await _viewSubject.GetReleaseViewAsync(_viewHierarchyId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetReleaseViewAsync_SurfacesScoringFields_WhenTierMatches()
    {
        // Arrange
        var tierListId = Guid.NewGuid();
        var tierId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        var hierarchyItem = new ContentHierarchyItem
        {
            Id = _viewHierarchyId,
            ContentType = "movie",
            Level = "movie",
            Slug = "example",
            Name = "Example",
            EffectiveQualityTierListId = tierListId,
            CreatedAt = createdAt,
        };
        _viewContentHierarchyItems
            .Setup(r => r.GetByIdAsync(_viewHierarchyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hierarchyItem);
        _viewContentHierarchyItems
            .Setup(r => r.GetLineageAsync(_viewHierarchyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([hierarchyItem]);

        var tier = new QualityTier
        {
            Id = tierId,
            Name = "Matched",
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
                                Value = "1080p",
                            },
                        },
                    },
                ],
            },
            CreatedAt = createdAt,
        };
        _viewQualityTierLists
            .Setup(r => r.GetByIdAsync(tierListId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualityTierList
            {
                Id = tierListId,
                Name = "List",
                CreatedAt = createdAt,
                QualityTierRanks =
                [
                    new QualityTierListRank
                    {
                        QualityTierListId = tierListId,
                        QualityTierId = tierId,
                        QualityTier = tier,
                        Ordinal = 2,
                        IsCutoff = true,
                        CreatedAt = createdAt,
                    },
                ],
            });
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Show.S01E01.1080p.WEB-DL",
            ExternalId = "external-1",
            PublishDate = createdAt,
            CreatedAt = createdAt,
        };
        _viewReleaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [release], TotalItems = 1 });

        // Act
        var result = await _viewSubject.GetReleaseViewAsync(_viewHierarchyId).ConfigureAwait(false);

        // Assert
        var row = result!.Single(r => r.Id == release.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Score, Is.EqualTo(2));
            Assert.That(row.IsAcceptable, Is.True);
            Assert.That(row.MeetsCutoff, Is.True);
        }
    }

    [Test]
    public async Task GetReleaseViewAsync_SurfacesFailedAcquisition_WhenReleaseHasFailedDownload()
    {
        // Arrange
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Movie.2024.1080p.WEB",
            ExternalId = "shared-external",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        _viewReleaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [release], TotalItems = 1 });
        var failedDownload = new Download
        {
            Id = Guid.NewGuid(),
            ReleaseId = release.Id,
            DownloadClientId = Guid.NewGuid(),
            ExternalId = string.Empty,
            Title = "grab",
            Status = DownloadStatus.Failed,
            FailureReason = DownloadFailureReason.ClientRejected,
            FailedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        _viewDownloadRepository
            .Setup(r => r.QueryAsync(
                It.Is<DownloadQuery>(query =>
                    query.ContentHierarchyItemIds != null &&
                    query.ContentHierarchyItemIds.Contains(_viewHierarchyId)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download> { Items = [failedDownload], TotalItems = 1 });

        // Act
        var result = await _viewSubject.GetReleaseViewAsync(_viewHierarchyId).ConfigureAwait(false);

        // Assert
        var row = result!.Single(r => r.Id == release.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.AcquisitionStatus, Is.EqualTo(DownloadStatus.Failed));
            Assert.That(row.FailureReason, Is.EqualTo(DownloadFailureReason.ClientRejected));
        }
    }

    [Test]
    public async Task GetReleaseViewAsync_LoadsAllDownloads_WhenDownloadsSpanMultiplePages()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Movie.2024.1080p.WEB",
            ExternalId = "page-two-release",
            PublishDate = now,
            CreatedAt = now
        };
        _viewReleaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [release], TotalItems = 1 });
        var firstPage = Enumerable.Range(0, 200)
            .Select(index => new Download
            {
                Id = Guid.NewGuid(),
                ReleaseId = Guid.NewGuid(),
                DownloadClientId = Guid.NewGuid(),
                ExternalId = $"download-{index}",
                Title = $"Download {index}",
                Status = DownloadStatus.Downloaded,
                CreatedAt = now.AddMinutes(-index)
            })
            .ToList();
        var pageTwoDownload = new Download
        {
            Id = Guid.NewGuid(),
            ReleaseId = release.Id,
            DownloadClientId = Guid.NewGuid(),
            ExternalId = "target-download",
            Title = "Target download",
            Status = DownloadStatus.Downloading,
            CreatedAt = now.AddHours(-6)
        };
        _viewDownloadRepository
            .Setup(r => r.QueryAsync(
                It.Is<DownloadQuery>(query =>
                    query.ContentHierarchyItemIds != null &&
                    query.ContentHierarchyItemIds.Contains(_viewHierarchyId)),
                It.IsAny<PagingParameter>(),
                It.Is<SortParameter>(sort =>
                    sort.PropertyName == nameof(Download.CreatedAt) &&
                    sort.Direction == SortDirection.Descending),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DownloadQuery _, PagingParameter paging, SortParameter? _, CancellationToken _) =>
                new PagedList<Download>
                {
                    Items = paging.PageNumber == 1 ? firstPage : [pageTwoDownload],
                    PageNumber = paging.PageNumber,
                    PageSize = paging.PageSize,
                    TotalItems = 201
                });

        // Act
        var result = await _viewSubject.GetReleaseViewAsync(_viewHierarchyId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Single().AcquisitionStatus, Is.EqualTo(DownloadStatus.Downloading));
            _viewDownloadRepository.Verify(
                repository => repository.QueryAsync(
                    It.IsAny<DownloadQuery>(),
                    It.Is<PagingParameter>(paging => paging.PageNumber == 2 && paging.PageSize == 200),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task GetReleaseViewAsync_DoesNotUseOlderTerminalDownload_WhenNewestDownloadIsActive()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Movie.2024.1080p.WEB",
            ExternalId = "missing-release",
            PublishDate = now,
            CreatedAt = now
        };
        _viewReleaseRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [release], TotalItems = 1 });
        var olderFailed = new Download
        {
            Id = Guid.NewGuid(),
            ReleaseId = release.Id,
            DownloadClientId = Guid.NewGuid(),
            ExternalId = "older-failed",
            Title = "Older failed download",
            Status = DownloadStatus.Failed,
            FailureReason = DownloadFailureReason.ClientRejected,
            CreatedAt = now.AddHours(-2)
        };
        var newerActive = new Download
        {
            Id = Guid.NewGuid(),
            ReleaseId = release.Id,
            DownloadClientId = Guid.NewGuid(),
            ExternalId = "newer-active",
            Title = "Newer active download",
            Status = DownloadStatus.Downloading,
            CreatedAt = now.AddHours(-1)
        };
        _viewDownloadRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<DownloadQuery>(query => query.ContentHierarchyItemIds != null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download> { Items = [] });
        _viewDownloadRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<DownloadQuery>(query =>
                    query.ReleaseIds != null &&
                    query.ReleaseIds.SequenceEqual(new[] { release.Id })),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 200),
                It.Is<SortParameter>(sort =>
                    sort.PropertyName == nameof(Download.CreatedAt) &&
                    sort.Direction == SortDirection.Descending),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items = [olderFailed, newerActive],
                TotalItems = 2
            });

        // Act
        var result = await _viewSubject.GetReleaseViewAsync(_viewHierarchyId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Single().AcquisitionStatus, Is.Null);
            Assert.That(result!.Single().FailureReason, Is.Null);
        }
    }

    [Test]
    public async Task GetReleaseViewAsync_IncludesDescendantReleases_WhenRootHasChildScope()
    {
        // Arrange
        var childId = Guid.NewGuid();
        _viewContentHierarchyItems
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = childId,
                        ParentId = _viewHierarchyId,
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "child",
                        Name = "Child",
                        CreatedAt = DateTime.UtcNow,
                    },
                ],
                TotalItems = 1,
            });
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Show.S01E01.1080p.WEB-DL",
            ExternalId = "descendant-external",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        _viewReleaseRepository
            .Setup(r => r.QueryAsync(
                It.Is<ReleaseQuery>(q =>
                    q.ContentHierarchyItemIds != null && q.ContentHierarchyItemIds.Contains(childId)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [release], TotalItems = 1 });

        // Act
        var result = await _viewSubject.GetReleaseViewAsync(_viewHierarchyId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Select(row => row.Id), Does.Contain(release.Id));
    }
}
