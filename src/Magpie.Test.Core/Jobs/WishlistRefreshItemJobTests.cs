using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Models.Paging;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs;

[TestFixture]
public sealed class WishlistRefreshItemJobTests
{
    private Mock<IWishlistRepository> _wishlistRepository = null!;
    private Mock<IContentFilterRepository> _contentFilterRepository = null!;
    private Mock<IMetadataSearchService> _metadataService = null!;
    private Mock<IContentService> _contentService = null!;
    private Mock<IWishlistDiscoveredRepository> _wishlistDiscoveredRepository = null!;
    private Mock<IUserRepository> _userRepository = null!;
    private Mock<IEventBus> _eventBus = null!;
    private IJob _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _wishlistRepository = new Mock<IWishlistRepository>();
        _contentFilterRepository = new Mock<IContentFilterRepository>();
        _metadataService = new Mock<IMetadataSearchService>();
        _contentService = new Mock<IContentService>();
        _wishlistDiscoveredRepository = new Mock<IWishlistDiscoveredRepository>();
        _userRepository = new Mock<IUserRepository>();
        _eventBus = new Mock<IEventBus>();

        _wishlistDiscoveredRepository
            .Setup(r => r.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _contentService
            .Setup(s => s.QueryAsync(
                It.IsAny<ContentQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                TotalItems = 0,
                PageNumber = 1,
                PageSize = 1
            });
        _subject = new WishlistRefreshItemJob(
            _wishlistRepository.Object,
            _contentFilterRepository.Object,
            _metadataService.Object,
            _contentService.Object,
            _wishlistDiscoveredRepository.Object,
            _userRepository.Object,
            _eventBus.Object,
            Mock.Of<IProgressReporter>(),
            TimeProvider.System,
            NullLogger<WishlistRefreshItemJob>.Instance);
    }

    [Test]
    public void ExecuteAsync_ThrowsArgumentException_WhenWishlistIdMissing()
    {
        // Arrange
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: Guid.NewGuid(),
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        // Act
        var ex = Assert.ThrowsAsync<ArgumentException>(() => _subject.ExecuteAsync(context));

        // Assert
        Assert.That(ex, Has.Property(nameof(ex.Message)).Contain("A wishlist ID is required."));
    }

    [Test]
    public void ExecuteAsync_ThrowsArgumentException_WhenWishlistIdNotGuid()
    {
        // Arrange
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: Guid.NewGuid(),
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["wishlistId"] = "not-a-guid" },
            Event: null);

        // Act
        var ex = Assert.ThrowsAsync<ArgumentException>(() => _subject.ExecuteAsync(context));

        // Assert
        Assert.That(ex, Has.Property(nameof(ex.Message)).Contain("The wishlist ID has an invalid format."));
    }

    [Test]
    public async Task ExecuteAsync_SkipsRefresh_WhenWishlistNotFound()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        _wishlistRepository
            .Setup(r => r.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Wishlist?)null);
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: Guid.NewGuid(),
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["wishlistId"] = wishlistId.ToString("D") },
            Event: null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _metadataService.Verify(
                s => s.SearchCandidatesAsync(
                    It.IsAny<ContentFilterCriteria>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _wishlistRepository.Verify(
                r => r.PatchAsync(
                    It.IsAny<WishlistPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsRefresh_WhenWishlistDisabled()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        _wishlistRepository
            .Setup(r => r.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist { Id = wishlistId, Name = "disabled", Enabled = false });
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: Guid.NewGuid(),
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["wishlistId"] = wishlistId.ToString("D") },
            Event: null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _metadataService.Verify(
                s => s.SearchCandidatesAsync(
                    It.IsAny<ContentFilterCriteria>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _wishlistRepository.Verify(
                r => r.PatchAsync(
                    It.IsAny<WishlistPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    [TestCase(null)]
    public async Task ExecuteAsync_PassesMonitoringToHydration_WhenMatchIsFound(bool? monitoring)
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var contentFilterId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _wishlistRepository
            .Setup(r => r.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = wishlistId,
                Name = "monitored",
                Enabled = true,
                Monitoring = monitoring,
                ContentFilterId = contentFilterId
            });
        _contentFilterRepository
            .Setup(r => r.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = contentFilterId,
                Name = "filter",
                Criteria = new ContentFilterCriteria
                {
                    ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
                    {
                        And =
                        [
                            new ContentHierarchyItemCriteria
                            {
                                ContentType = new CriteriaSet<PropertyCriterion<string>>
                                {
                                    And = [new PropertyCriterion<string> { Value = "type-b" }]
                                },
                                Metadata = new CriteriaSet<MetadataCriteria>
                                {
                                    And =
                                    [
                                        new MetadataCriteria
                                        {
                                            Title = new CriteriaSet<PropertyCriterion<string>>
                                            {
                                                And = [new PropertyCriterion<string> { Value = "Match" }]
                                            }
                                        }
                                    ]
                                }
                            }
                        ]
                    }
                }
            });

        var truncatedPage = ProviderMetadataCandidateSearchResult.Empty with
        {
            PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = true }
        };
        var pageTwo = new ProviderMetadataCandidateSearchResult
        {
            Matches =
            [
                new MetadataSearchMatch
                {
                    Candidate = new MetadataSearchCandidate
                    {
                        ProviderType = "provider-a",
                        ExternalIdSource = "provider-a",
                        ExternalId = "42",
                        ContentType = "type-b",
                        Level = "movie",
                        Title = "Match"
                    },
                    InLibrary = false
                }
            ],
            PageItemCount = 1,
            PageInfo = new ProviderSearchPageInfo { RequestedPage = 2, HasMore = false }
        };
        _metadataService
            .SetupSequence(s => s.SearchCandidatesAsync(
                It.Is<ContentFilterCriteria>(criteria =>
                    criteria.ContentHierarchyItem.And.Single().Metadata.And.Single().Title.And.Single().Value ==
                        "Match"),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(truncatedPage)
            .ReturnsAsync(pageTwo);

        _contentService
            .Setup(s => s.HydrateFromExternalIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<bool?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "movie",
                Level = "movie",
                Slug = "movie",
                Name = "Movie"
            });

        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["wishlistId"] = wishlistId.ToString("D") },
            Event: null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _contentService.Verify(
                s => s.HydrateFromExternalIdAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    "42",
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<Guid?>(),
                    monitoring,
                    It.IsAny<Guid?>(),
                    wishlistId,
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _wishlistDiscoveredRepository.Verify(
                r => r.RecordDiscoveredAsync(
                    wishlistId,
                    It.IsAny<IEnumerable<WishlistDiscovered>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _eventBus.Verify(b => b.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()), Times.Once);
            _wishlistRepository.Verify(
                r => r.PatchAsync(
                    It.Is<WishlistPatch>(patch => patch.Id == wishlistId && patch.LastSyncedAt != null),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _metadataService.Verify(
                s => s.SearchCandidatesAsync(
                    It.Is<ContentFilterCriteria>(criteria =>
                        criteria.ContentHierarchyItem.And.Single().Metadata.And.Single().Title.And.Single().Value ==
                            "Match"),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
    }

    [Test]
    public async Task ExecuteAsync_HydratesAllMatches_WhenProviderCompletesAfterFormerCaps()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var contentFilterId = Guid.NewGuid();
        _wishlistRepository
            .Setup(r => r.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = wishlistId,
                Name = "uncapped",
                Enabled = true,
                ContentFilterId = contentFilterId
            });
        _contentFilterRepository
            .Setup(r => r.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = contentFilterId,
                Name = "filter",
                Criteria = new ContentFilterCriteria()
            });
        _metadataService
            .Setup(s => s.SearchCandidatesAsync(
                It.IsAny<ContentFilterCriteria>(),
                string.Empty,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentFilterCriteria _, string _, int page, CancellationToken _) =>
                new ProviderMetadataCandidateSearchResult
                {
                    Matches =
                    [
                        .. Enumerable.Range(((page - 1) * 40) + 1, 40)
                            .Select(value => new MetadataSearchMatch
                            {
                                Candidate = new MetadataSearchCandidate
                                {
                                    ProviderType = "provider-a",
                                    ExternalIdSource = "provider-a",
                                    ExternalId = value.ToString(CultureInfo.InvariantCulture),
                                    ContentType = "movie",
                                    Level = "movie",
                                    Title = "Match"
                                },
                                InLibrary = false
                            })
                    ],
                    PageItemCount = 40,
                    PageInfo = new ProviderSearchPageInfo { RequestedPage = page, HasMore = page < 51 }
                });
        _contentService
            .Setup(s => s.HydrateFromExternalIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<bool?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "movie",
                Level = "movie",
                Slug = "movie",
                Name = "Movie"
            });
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: Guid.NewGuid(),
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["wishlistId"] = wishlistId.ToString("D") },
            Event: null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _metadataService.Verify(
                s => s.SearchCandidatesAsync(
                    It.IsAny<ContentFilterCriteria>(),
                    string.Empty,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(51));
            _metadataService.Verify(
                s => s.SearchCandidatesAsync(
                    It.IsAny<ContentFilterCriteria>(),
                    string.Empty,
                    51,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentService.Verify(
                s => s.HydrateFromExternalIdAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<bool?>(),
                    It.IsAny<Guid?>(),
                    wishlistId,
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2040));
            _wishlistDiscoveredRepository.Verify(
                r => r.ListAsync(wishlistId, It.IsAny<CancellationToken>()),
                Times.Once);
            _wishlistDiscoveredRepository.Verify(
                r => r.RecordDiscoveredAsync(
                    wishlistId,
                    It.Is<IEnumerable<WishlistDiscovered>>(items => items.Count() == 1),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2040));
            _wishlistRepository.Verify(
                r => r.PatchAsync(
                    It.Is<WishlistPatch>(patch => patch.Id == wishlistId && patch.LastSyncedAt != null),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public void ExecuteAsync_ThrowsOperationCanceledException_WhenHydrationCancelsTokenAndReturns()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var contentFilterId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        _wishlistRepository
            .Setup(r => r.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = wishlistId,
                Name = "canceled",
                Enabled = true,
                ContentFilterId = contentFilterId
            });
        _contentFilterRepository
            .Setup(r => r.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = contentFilterId,
                Name = "filter",
                Criteria = new ContentFilterCriteria()
            });
        _metadataService
            .Setup(s => s.SearchCandidatesAsync(
                It.IsAny<ContentFilterCriteria>(),
                string.Empty,
                1,
                It.Is<CancellationToken>(token => token == cancellation.Token)))
            .ReturnsAsync(new ProviderMetadataCandidateSearchResult
            {
                Matches =
                [
                    new MetadataSearchMatch
                    {
                        Candidate = new MetadataSearchCandidate
                        {
                            ProviderType = "provider-a",
                            ExternalIdSource = "provider-a",
                            ExternalId = "canceled",
                            ContentType = "movie",
                            Level = "movie",
                            Title = "Canceled"
                        },
                        InLibrary = false
                    }
                ],
                PageItemCount = 1,
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        _contentService
            .Setup(s => s.HydrateFromExternalIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                "canceled",
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<bool?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<Guid?>(),
                It.Is<CancellationToken>(token => token == cancellation.Token)))
            .Callback(() => cancellation.Cancel())
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "movie",
                Level = "movie",
                Slug = "movie",
                Name = "Canceled"
            });
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: Guid.NewGuid(),
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["wishlistId"] = wishlistId.ToString("D") },
            Event: null);

        // Act
        var exception = Assert.ThrowsAsync<OperationCanceledException>(
            () => _subject.ExecuteAsync(context, cancellation.Token));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Is.Not.Null);
            _wishlistRepository.Verify(
                r => r.PatchAsync(
                    It.IsAny<WishlistPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _wishlistDiscoveredRepository.Verify(
                r => r.RecordDiscoveredAsync(
                    wishlistId,
                    It.Is<IEnumerable<WishlistDiscovered>>(items =>
                        items.Single().ExternalId == "canceled"),
                    CancellationToken.None),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_ContinuesRefresh_WhenCandidateHydrationFails()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var contentFilterId = Guid.NewGuid();
        _wishlistRepository
            .Setup(r => r.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = wishlistId,
                Name = "isolated",
                Enabled = true,
                ContentFilterId = contentFilterId
            });
        _contentFilterRepository
            .Setup(r => r.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = contentFilterId,
                Name = "filter",
                Criteria = new ContentFilterCriteria()
            });
        _metadataService
            .Setup(s => s.SearchCandidatesAsync(
                It.IsAny<ContentFilterCriteria>(),
                string.Empty,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderMetadataCandidateSearchResult
            {
                Matches =
                [
                    new MetadataSearchMatch
                    {
                        Candidate = new MetadataSearchCandidate
                        {
                            ProviderType = "provider-a",
                            ExternalIdSource = "provider-a",
                            ExternalId = "bad",
                            ContentType = "movie",
                            Level = "movie",
                            Title = "Bad"
                        },
                        InLibrary = false
                    },
                    new MetadataSearchMatch
                    {
                        Candidate = new MetadataSearchCandidate
                        {
                            ProviderType = "provider-a",
                            ExternalIdSource = "provider-a",
                            ExternalId = "good",
                            ContentType = "movie",
                            Level = "movie",
                            Title = "Good"
                        },
                        InLibrary = false
                    }
                ],
                PageItemCount = 2,
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        _contentService
            .Setup(s => s.HydrateFromExternalIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                "bad",
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<bool?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("failure"));
        _contentService
            .Setup(s => s.HydrateFromExternalIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                "good",
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<bool?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "movie",
                Level = "movie",
                Slug = "movie",
                Name = "Good"
            });
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: Guid.NewGuid(),
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["wishlistId"] = wishlistId.ToString("D") },
            Event: null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _contentService.Verify(
                s => s.HydrateFromExternalIdAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<bool?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            _wishlistDiscoveredRepository.Verify(
                r => r.RecordDiscoveredAsync(
                    wishlistId,
                    It.Is<IEnumerable<WishlistDiscovered>>(items =>
                        items.Single().ExternalId == "bad"),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _wishlistDiscoveredRepository.Verify(
                r => r.RecordDiscoveredAsync(
                    wishlistId,
                    It.Is<IEnumerable<WishlistDiscovered>>(items =>
                        items.Single().ExternalId == "good"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _eventBus.Verify(
                b => b.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _wishlistRepository.Verify(
                r => r.PatchAsync(
                    It.Is<WishlistPatch>(patch => patch.Id == wishlistId && patch.LastSyncedAt != null),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public void ExecuteAsync_RecordsProcessedCandidate_WhenLaterSearchPageFails()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var contentFilterId = Guid.NewGuid();
        _wishlistRepository
            .Setup(r => r.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = wishlistId,
                Name = "interrupted",
                Enabled = true,
                ContentFilterId = contentFilterId
            });
        _contentFilterRepository
            .Setup(r => r.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = contentFilterId,
                Name = "filter",
                Criteria = new ContentFilterCriteria()
            });
        _metadataService
            .SetupSequence(s => s.SearchCandidatesAsync(
                It.IsAny<ContentFilterCriteria>(),
                string.Empty,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderMetadataCandidateSearchResult
            {
                Matches =
                [
                    new MetadataSearchMatch
                    {
                        Candidate = new MetadataSearchCandidate
                        {
                            ProviderType = "provider-a",
                            ExternalIdSource = "provider-a",
                            ExternalId = "first",
                            ContentType = "movie",
                            Level = "movie",
                            Title = "First"
                        },
                        InLibrary = false
                    }
                ],
                PageItemCount = 1,
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = true }
            })
            .ThrowsAsync(new InvalidOperationException("The search failed."));
        _contentService
            .Setup(s => s.HydrateFromExternalIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                "first",
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<bool?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "movie",
                Level = "movie",
                Slug = "movie",
                Name = "First"
            });
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: Guid.NewGuid(),
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["wishlistId"] = wishlistId.ToString("D") },
            Event: null);

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => _subject.ExecuteAsync(context));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Is.Not.Null);
            _wishlistDiscoveredRepository.Verify(
                r => r.RecordDiscoveredAsync(
                    wishlistId,
                    It.Is<IEnumerable<WishlistDiscovered>>(items =>
                        items.Single().ExternalId == "first"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentService.Verify(
                s => s.HydrateFromExternalIdAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    "first",
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<bool?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _wishlistRepository.Verify(
                r => r.PatchAsync(
                    It.IsAny<WishlistPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public void ExecuteAsync_ThrowsAndSkipsLastSyncedAt_WhenDiscoveryRecordFails()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var contentFilterId = Guid.NewGuid();
        _wishlistRepository
            .Setup(r => r.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = wishlistId,
                Name = "ledger-down",
                Enabled = true,
                ContentFilterId = contentFilterId
            });
        _contentFilterRepository
            .Setup(r => r.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = contentFilterId,
                Name = "filter",
                Criteria = new ContentFilterCriteria()
            });
        _metadataService
            .Setup(s => s.SearchCandidatesAsync(
                It.IsAny<ContentFilterCriteria>(),
                string.Empty,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderMetadataCandidateSearchResult
            {
                Matches =
                [
                    new MetadataSearchMatch
                    {
                        Candidate = new MetadataSearchCandidate
                        {
                            ProviderType = "provider-a",
                            ExternalIdSource = "provider-a",
                            ExternalId = "unrecorded",
                            ContentType = "movie",
                            Level = "movie",
                            Title = "Unrecorded"
                        },
                        InLibrary = false
                    }
                ],
                PageItemCount = 1,
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        _contentService
            .Setup(s => s.HydrateFromExternalIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                "unrecorded",
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<bool?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "movie",
                Level = "movie",
                Slug = "movie",
                Name = "Unrecorded"
            });
        _wishlistDiscoveredRepository
            .Setup(r => r.RecordDiscoveredAsync(
                It.IsAny<Guid>(),
                It.IsAny<IEnumerable<WishlistDiscovered>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The ledger write failed."));
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: Guid.NewGuid(),
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["wishlistId"] = wishlistId.ToString("D") },
            Event: null);

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => _subject.ExecuteAsync(context));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Is.Not.Null);
            _contentService.Verify(
                s => s.HydrateFromExternalIdAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    "unrecorded",
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<bool?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _wishlistDiscoveredRepository.Verify(
                r => r.RecordDiscoveredAsync(
                    wishlistId,
                    It.Is<IEnumerable<WishlistDiscovered>>(items =>
                        items.Single().ExternalId == "unrecorded"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _wishlistRepository.Verify(
                r => r.PatchAsync(
                    It.IsAny<WishlistPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_RetriesCandidate_WhenFormerHydrationFailed()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var contentFilterId = Guid.NewGuid();
        var ledger = new List<WishlistDiscovered>();
        _wishlistDiscoveredRepository
            .Setup(r => r.ListAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ledger);
        _wishlistDiscoveredRepository
            .Setup(r => r.RecordDiscoveredAsync(
                wishlistId,
                It.IsAny<IEnumerable<WishlistDiscovered>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, IEnumerable<WishlistDiscovered>, CancellationToken>(
                (_, candidates, _) => ledger.AddRange(candidates));
        _wishlistRepository
            .Setup(r => r.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = wishlistId,
                Name = "flaky",
                Enabled = true,
                ContentFilterId = contentFilterId
            });
        _contentFilterRepository
            .Setup(r => r.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = contentFilterId,
                Name = "filter",
                Criteria = new ContentFilterCriteria()
            });
        _metadataService
            .Setup(s => s.SearchCandidatesAsync(
                It.IsAny<ContentFilterCriteria>(),
                string.Empty,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderMetadataCandidateSearchResult
            {
                Matches =
                [
                    new MetadataSearchMatch
                    {
                        Candidate = new MetadataSearchCandidate
                        {
                            ProviderType = "provider-a",
                            ExternalIdSource = "provider-a",
                            ExternalId = "flaky",
                            ContentType = "movie",
                            Level = "movie",
                            Title = "Flaky"
                        },
                        InLibrary = false
                    }
                ],
                PageItemCount = 1,
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        _contentService
            .SetupSequence(s => s.HydrateFromExternalIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                "flaky",
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<bool?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The provider failed."))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "movie",
                Level = "movie",
                Slug = "movie",
                Name = "Flaky"
            });
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: Guid.NewGuid(),
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["wishlistId"] = wishlistId.ToString("D") },
            Event: null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);
        var recordedAfterFailedRun = ledger.Count;
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(recordedAfterFailedRun, Is.Zero);
            Assert.That(ledger, Has.Count.EqualTo(1));
            _contentService.Verify(
                s => s.HydrateFromExternalIdAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    "flaky",
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<bool?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            _wishlistDiscoveredRepository.Verify(
                r => r.RecordDiscoveredAsync(
                    wishlistId,
                    It.Is<IEnumerable<WishlistDiscovered>>(items =>
                        items.Single().ExternalId == "flaky"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _eventBus.Verify(
                b => b.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_AddsNoContent_WhenCandidateIsAlreadyDiscovered()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var contentFilterId = Guid.NewGuid();
        _wishlistDiscoveredRepository
            .Setup(r => r.ListAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new WishlistDiscovered
                {
                    Id = Guid.NewGuid(),
                    WishlistId = wishlistId,
                    ExternalIdSource = "provider-a",
                    ExternalId = "removed",
                    Title = "Removed"
                }
            ]);
        _wishlistRepository
            .Setup(r => r.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = wishlistId,
                Name = "already-seen",
                Enabled = true,
                ContentFilterId = contentFilterId
            });
        _contentFilterRepository
            .Setup(r => r.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = contentFilterId,
                Name = "filter",
                Criteria = new ContentFilterCriteria()
            });
        _metadataService
            .Setup(s => s.SearchCandidatesAsync(
                It.IsAny<ContentFilterCriteria>(),
                string.Empty,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderMetadataCandidateSearchResult
            {
                Matches =
                [
                    new MetadataSearchMatch
                    {
                        Candidate = new MetadataSearchCandidate
                        {
                            ProviderType = "provider-a",
                            ExternalIdSource = "provider-a",
                            ExternalId = "removed",
                            ContentType = "movie",
                            Level = "movie",
                            Title = "Removed"
                        },
                        InLibrary = false
                    }
                ],
                PageItemCount = 1,
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: Guid.NewGuid(),
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["wishlistId"] = wishlistId.ToString("D") },
            Event: null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _contentService.Verify(
                s => s.HydrateFromExternalIdAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<bool?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _wishlistDiscoveredRepository.Verify(
                r => r.RecordDiscoveredAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<IEnumerable<WishlistDiscovered>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _wishlistRepository.Verify(
                r => r.PatchAsync(
                    It.Is<WishlistPatch>(patch => patch.Id == wishlistId && patch.LastSyncedAt != null),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestCase("tv", 0, 1)]
    [TestCase("movie", 1, 0)]
    public async Task ExecuteAsync_HydratesMatch_WhenExternalIdExistsForContentType(
        string existingContentType,
        int existingCount,
        int expectedHydrations)
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var contentFilterId = Guid.NewGuid();
        _wishlistRepository
            .Setup(r => r.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = wishlistId,
                Name = "collision",
                Enabled = true,
                ContentFilterId = contentFilterId
            });
        _contentFilterRepository
            .Setup(r => r.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = contentFilterId,
                Name = "filter",
                Criteria = new ContentFilterCriteria()
            });
        _metadataService
            .Setup(s => s.SearchCandidatesAsync(
                It.IsAny<ContentFilterCriteria>(),
                string.Empty,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderMetadataCandidateSearchResult
            {
                Matches =
                [
                    new MetadataSearchMatch
                    {
                        Candidate = new MetadataSearchCandidate
                        {
                            ProviderType = "provider-b",
                            ExternalIdSource = "provider-b",
                            ExternalId = "1003598",
                            ContentType = "movie",
                            Level = "movie",
                            Title = "Collision"
                        },
                        InLibrary = false
                    }
                ],
                PageItemCount = 1,
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        _contentService
            .Setup(s => s.QueryAsync(
                It.Is<ContentQuery>(query =>
                    query.Criteria!.ContentHierarchyItem.And.Single().ContentType.And.Single().Value == "movie" &&
                    query.Criteria.ContentHierarchyItem.And.Single().Metadata.And.Single().ExternalIds.Some.Single()
                        .Single().Source.And.Single().Value == "provider-b" &&
                    query.Criteria.ContentHierarchyItem.And.Single().Metadata.And.Single().ExternalIds.Some.Single()
                        .Single().Value.And.Single().Value == "1003598"),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items = [],
                TotalItems = existingContentType == "movie" ? existingCount : 0,
                PageNumber = 1,
                PageSize = 1
            });
        _contentService
            .Setup(s => s.HydrateFromExternalIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<bool?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "movie",
                Level = "movie",
                Slug = "collision",
                Name = "Collision"
            });
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: Guid.NewGuid(),
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["wishlistId"] = wishlistId.ToString("D") },
            Event: null);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _contentService.Verify(
            s => s.HydrateFromExternalIdAsync(
                "provider-b",
                "provider-b",
                "1003598",
                "movie",
                "movie",
                It.IsAny<Guid?>(),
                null,
                It.IsAny<Guid?>(),
                wishlistId,
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(expectedHydrations));
    }
}
