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

    [Test]
    public async Task ExecuteAsync_HydratesMatch_WhenFirstPageIsTruncated()
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
                    It.IsAny<bool?>(),
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
                    It.Is<IEnumerable<WishlistDiscovered>>(items => items.Count() == 2040),
                    It.IsAny<CancellationToken>()),
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
                    It.IsAny<Guid>(),
                    It.IsAny<IEnumerable<WishlistDiscovered>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
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
    public void Spec_RequiresWishlistId_AndPartitionsPerTarget()
    {
        // Arrange
        // Act
        var spec = WishlistRefreshItemJob.Spec;
        var parameter = spec.Parameters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spec.JobType, Is.EqualTo("WishlistRefreshItem"));
            Assert.That(spec.DisplayName, Is.EqualTo("Wishlist refresh item"));
            Assert.That(spec.ConcurrencyScope, Is.EqualTo(ConcurrencyScope.PerTypeAndTarget));
            Assert.That(spec.LockPartitionFields, Is.EqualTo(["wishlistId"]));
            Assert.That(parameter.Name, Is.EqualTo("wishlistId"));
            Assert.That(parameter.Required, Is.True);
            Assert.That(parameter.Description, Does.StartWith("The wishlist to refresh."));
        }
    }
}
