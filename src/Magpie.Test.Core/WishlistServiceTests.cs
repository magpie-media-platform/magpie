using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;
using Magpie.Models.Patch;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class WishlistServiceTests
{
    private static readonly Guid OwnerId = Guid.Parse("3a0f7f2c-1e4b-4d0a-9b1c-2f6d8e4a7c10");
    private static readonly Guid AdminId = Guid.Parse("3a0f7f2c-1e4b-4d0a-9b1c-2f6d8e4a7c11");
    private static readonly Guid WishlistId = Guid.Parse("3a0f7f2c-1e4b-4d0a-9b1c-2f6d8e4a7c12");
    private static readonly Guid SharedFilterId = Guid.Parse("3a0f7f2c-1e4b-4d0a-9b1c-2f6d8e4a7c13");
    private static readonly Guid BackingFilterId = Guid.Parse("3a0f7f2c-1e4b-4d0a-9b1c-2f6d8e4a7c14");
    private static readonly Guid OtherWishlistId = Guid.Parse("3a0f7f2c-1e4b-4d0a-9b1c-2f6d8e4a7c15");
    private static readonly Guid OtherUserId = Guid.Parse("09a1dfdb-f7fc-4012-8433-e02e8d219f7f");
    private static readonly Guid ReferencedFilterId = Guid.Parse("d8defef7-2a87-45e8-8a83-11a502308699");

    private Mock<IWishlistRepository> _wishlists = null!;
    private Mock<IContentFilterRepository> _contentFilters = null!;
    private IWishlistService _subject = null!;

    public static IEnumerable<TestCaseData> ReferenceableFilters()
    {
        yield return new TestCaseData(null, OwnerId, false);
        yield return new TestCaseData((Guid?)OwnerId, OwnerId, false);
        yield return new TestCaseData(null, AdminId, true);
        yield return new TestCaseData((Guid?)OwnerId, AdminId, true);
    }

    public static IEnumerable<TestCaseData> UnreferenceableFilters()
    {
        yield return new TestCaseData((Guid?)OtherUserId, OwnerId, false);
        yield return new TestCaseData((Guid?)AdminId, AdminId, true);
    }

    public static IEnumerable<TestCaseData> ReadableFilterCallers()
    {
        yield return new TestCaseData(null, OwnerId, false);
        yield return new TestCaseData((Guid?)OwnerId, OwnerId, false);
        yield return new TestCaseData((Guid?)OtherUserId, AdminId, true);
    }

    public static IEnumerable<TestCaseData> DeletableFilters()
    {
        yield return new TestCaseData((Guid?)OwnerId, false, OwnerId, false);
        yield return new TestCaseData((Guid?)OwnerId, true, OwnerId, false);
        yield return new TestCaseData(null, false, AdminId, true);
    }

    public static IEnumerable<TestCaseData> UndeletableFilters()
    {
        yield return new TestCaseData((Guid?)OtherUserId, false, OwnerId, false);
        yield return new TestCaseData(null, false, OwnerId, false);
        yield return new TestCaseData((Guid?)OwnerId, true, AdminId, true);
    }

    [SetUp]
    public void SetUp()
    {
        _wishlists = new Mock<IWishlistRepository> { CallBase = true };
        _wishlists
            .Setup(repository => repository.PatchAsync(
                It.IsAny<WishlistPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _wishlists
            .Setup(repository => repository.DeleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _contentFilters = new Mock<IContentFilterRepository> { CallBase = true };
        _contentFilters
            .Setup(repository => repository.CreateAsync(
                It.IsAny<ContentFilter>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentFilter filter, Guid? _, CancellationToken _) => filter);
        _contentFilters
            .Setup(repository => repository.PatchAsync(
                It.IsAny<ContentFilterPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentFilters
            .Setup(repository => repository.DeleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentFilters
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentFilterQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentFilter> { Items = [], PageNumber = 1, PageSize = 500, TotalItems = 0 });

        var transactions = new Mock<ITransactionManager>();
        transactions
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken token) => operation(token));
        transactions
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Wishlist>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Wishlist>> operation, CancellationToken token) => operation(token));

        _subject = new WishlistService(
            _wishlists.Object,
            Mock.Of<IWishlistDiscoveredRepository>(),
            _contentFilters.Object,
            new FilterService(_contentFilters.Object, Mock.Of<ISubjectFilterRepository>()),
            transactions.Object);
    }

    [Test]
    public async Task PatchAsync_ForksAPrivateBackingFilter_WhenTheWishlistPointsAtASharedFilter()
    {
        // Arrange
        _wishlists
            .Setup(repository => repository.GetByIdAsync(WishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = WishlistId,
                Name = "Wanted",
                OwnerUserId = OwnerId,
                ContentFilterId = SharedFilterId
            });
        _contentFilters
            .Setup(repository => repository.GetByIdAsync(SharedFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = SharedFilterId,
                Name = "Everyone",
                Criteria = new ContentFilterCriteria(),
                OwnerUserId = null
            });
        var patch = new WishlistPatch
        {
            Criteria = new ScalarPatch<ContentFilterCriteria> { Value = new ContentFilterCriteria() }
        };

        // Act
        var patched = await _subject.PatchAsync(WishlistId, patch, OwnerId, false).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Is.True);
            _contentFilters.Verify(
                repository => repository.PatchAsync(
                    It.IsAny<ContentFilterPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _contentFilters.Verify(
                repository => repository.CreateAsync(
                    It.Is<ContentFilter>(filter => filter.OwnerUserId == OwnerId),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _wishlists.Verify(
                repository => repository.PatchAsync(
                    It.Is<WishlistPatch>(value =>
                        value.ContentFilterId != null &&
                        value.ContentFilterId.Value != SharedFilterId),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenOwnerLockBlocksAnAdministrator()
    {
        // Arrange
        _wishlists
            .Setup(repository => repository.GetByIdAsync(WishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = WishlistId,
                Name = "Wanted",
                OwnerUserId = OwnerId,
                OwnerLock = true,
                ContentFilterId = SharedFilterId
            });
        var patch = new WishlistPatch { Name = new ScalarPatch<string> { Value = "Renamed" } };

        // Act
        var patched = await _subject.PatchAsync(WishlistId, patch, AdminId, true).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Is.False);
            _wishlists.Verify(
                repository => repository.PatchAsync(
                    It.IsAny<WishlistPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public void CreateAsync_Throws_WhenTheReferencedContentFilterBelongsToAnotherUser()
    {
        // Arrange
        _contentFilters
            .Setup(repository => repository.GetByIdAsync(SharedFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = SharedFilterId,
                Name = "Private",
                Criteria = new ContentFilterCriteria(),
                OwnerUserId = AdminId
            });
        var create = new WishlistCreate { Name = "Wanted", ContentFilterId = SharedFilterId };

        // Act
        // Assert
        Assert.That(
            async () => await _subject.CreateAsync(create, OwnerId).ConfigureAwait(false),
            Throws.TypeOf<MagpieException>().With.Message.EqualTo("The content filter was not found."));
    }

    [TestCaseSource(nameof(UnreferenceableFilters))]
    public void PatchAsync_Throws_WhenTheCallerOrTheOwnerCannotReadTheContentFilter(
        Guid? filterOwnerUserId,
        Guid callerUserId,
        bool callerIsAdmin)
    {
        // Arrange
        _wishlists
            .Setup(repository => repository.GetByIdAsync(WishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = WishlistId,
                Name = "Wanted",
                OwnerUserId = OwnerId,
                ContentFilterId = BackingFilterId
            });
        _contentFilters
            .Setup(repository => repository.GetByIdAsync(ReferencedFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = ReferencedFilterId,
                Name = "Private",
                Criteria = new ContentFilterCriteria(),
                OwnerUserId = filterOwnerUserId,
                OwnerLock = true
            });
        var patch = new WishlistPatch { ContentFilterId = new ScalarPatch<Guid?> { Value = ReferencedFilterId } };

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await _subject.PatchAsync(WishlistId, patch, callerUserId, callerIsAdmin)
                    .ConfigureAwait(false),
                Throws.TypeOf<MagpieException>().With.Message.EqualTo("The content filter was not found."));
            _wishlists.Verify(
                repository => repository.PatchAsync(
                    It.IsAny<WishlistPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCaseSource(nameof(ReferenceableFilters))]
    public async Task PatchAsync_ChangesTheContentFilter_WhenTheCallerAndTheOwnerCanReadIt(
        Guid? filterOwnerUserId,
        Guid callerUserId,
        bool callerIsAdmin)
    {
        // Arrange
        _wishlists
            .Setup(repository => repository.GetByIdAsync(WishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = WishlistId,
                Name = "Wanted",
                OwnerUserId = OwnerId,
                ContentFilterId = BackingFilterId
            });
        _contentFilters
            .Setup(repository => repository.GetByIdAsync(ReferencedFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = ReferencedFilterId,
                Name = "Readable",
                Criteria = new ContentFilterCriteria(),
                OwnerUserId = filterOwnerUserId
            });
        var patch = new WishlistPatch { ContentFilterId = new ScalarPatch<Guid?> { Value = ReferencedFilterId } };

        // Act
        var patched = await _subject.PatchAsync(WishlistId, patch, callerUserId, callerIsAdmin).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Is.True);
            _wishlists.Verify(
                repository => repository.PatchAsync(
                    It.Is<WishlistPatch>(value =>
                        value.ContentFilterId != null &&
                        value.ContentFilterId.Value == ReferencedFilterId),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task PatchAsync_ForksAPrivateBackingFilter_WhenTheFilterOwnerLockBlocksAnAdministrator()
    {
        // Arrange
        var wishlist = new Wishlist
        {
            Id = WishlistId,
            Name = "Wanted",
            OwnerUserId = OwnerId,
            ContentFilterId = BackingFilterId
        };
        _wishlists
            .Setup(repository => repository.GetByIdAsync(WishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wishlist);
        _wishlists
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([wishlist]);
        _contentFilters
            .Setup(repository => repository.GetByIdAsync(BackingFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = BackingFilterId,
                Name = "Wanted (wishlist)",
                Criteria = new ContentFilterCriteria(),
                OwnerUserId = OwnerId,
                OwnerLock = true
            });
        var patch = new WishlistPatch
        {
            Criteria = new ScalarPatch<ContentFilterCriteria> { Value = new ContentFilterCriteria() }
        };

        // Act
        var patched = await _subject.PatchAsync(WishlistId, patch, AdminId, true).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Is.True);
            _contentFilters.Verify(
                repository => repository.PatchAsync(
                    It.IsAny<ContentFilterPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _wishlists.Verify(
                repository => repository.PatchAsync(
                    It.Is<WishlistPatch>(value =>
                        value.ContentFilterId != null &&
                        value.ContentFilterId.Value != BackingFilterId),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task PatchAsync_ForksAPrivateBackingFilter_WhenAnotherWishlistSharesTheBackingFilter()
    {
        // Arrange
        var wishlist = new Wishlist
        {
            Id = WishlistId,
            Name = "Wanted",
            OwnerUserId = OwnerId,
            ContentFilterId = BackingFilterId
        };
        _wishlists
            .Setup(repository => repository.GetByIdAsync(WishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wishlist);
        _wishlists
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                wishlist,
                new Wishlist
                {
                    Id = OtherWishlistId,
                    Name = "Also wanted",
                    OwnerUserId = OwnerId,
                    ContentFilterId = BackingFilterId
                }
            ]);
        _contentFilters
            .Setup(repository => repository.GetByIdAsync(BackingFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = BackingFilterId,
                Name = "Wanted (wishlist)",
                Criteria = new ContentFilterCriteria(),
                OwnerUserId = OwnerId
            });
        var patch = new WishlistPatch
        {
            Criteria = new ScalarPatch<ContentFilterCriteria> { Value = new ContentFilterCriteria() }
        };

        // Act
        var patched = await _subject.PatchAsync(WishlistId, patch, OwnerId, false).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Is.True);
            _contentFilters.Verify(
                repository => repository.PatchAsync(
                    It.IsAny<ContentFilterPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _wishlists.Verify(
                repository => repository.PatchAsync(
                    It.Is<WishlistPatch>(value =>
                        value.ContentFilterId != null &&
                        value.ContentFilterId.Value != BackingFilterId),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestCase("Wanted (wishlist)")]
    [TestCase("Renamed by the user")]
    public async Task PatchAsync_PatchesTheBackingFilter_WhenItBacksOnlyThisWishlist(string filterName)
    {
        // Arrange
        var wishlist = new Wishlist
        {
            Id = WishlistId,
            Name = "Wanted",
            OwnerUserId = OwnerId,
            ContentFilterId = BackingFilterId
        };
        _wishlists
            .Setup(repository => repository.GetByIdAsync(WishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wishlist);
        _wishlists
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([wishlist]);
        _contentFilters
            .Setup(repository => repository.GetByIdAsync(BackingFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = BackingFilterId,
                Name = filterName,
                Criteria = new ContentFilterCriteria(),
                OwnerUserId = OwnerId
            });
        var patch = new WishlistPatch
        {
            Criteria = new ScalarPatch<ContentFilterCriteria> { Value = new ContentFilterCriteria() }
        };

        // Act
        var patched = await _subject.PatchAsync(WishlistId, patch, OwnerId, false).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Is.True);
            _contentFilters.Verify(
                repository => repository.PatchAsync(
                    It.Is<ContentFilterPatch>(value => value.Id == BackingFilterId),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentFilters.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<ContentFilter>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task DeleteAsync_KeepsTheContentFilter_WhenTheCallerDoesNotAskToDeleteIt()
    {
        // Arrange
        _wishlists
            .Setup(repository => repository.GetByIdAsync(WishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = WishlistId,
                Name = "Wanted",
                OwnerUserId = OwnerId,
                ContentFilterId = BackingFilterId
            });

        // Act
        var deleted = await _subject.DeleteAsync(WishlistId, false, OwnerId, false).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            _contentFilters.Verify(
                repository => repository.DeleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCaseSource(nameof(DeletableFilters))]
    public async Task DeleteAsync_DeletesTheContentFilter_WhenTheCallerCanModifyIt(
        Guid? filterOwnerUserId,
        bool filterOwnerLock,
        Guid callerUserId,
        bool callerIsAdmin)
    {
        // Arrange
        _wishlists
            .Setup(repository => repository.GetByIdAsync(WishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = WishlistId,
                Name = "Wanted",
                OwnerUserId = OwnerId,
                ContentFilterId = BackingFilterId
            });
        _contentFilters
            .Setup(repository => repository.GetByIdAsync(BackingFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = BackingFilterId,
                Name = "Wanted (wishlist)",
                Criteria = new ContentFilterCriteria(),
                OwnerUserId = filterOwnerUserId,
                OwnerLock = filterOwnerLock
            });

        // Act
        var deleted = await _subject.DeleteAsync(WishlistId, true, callerUserId, callerIsAdmin).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            _wishlists.Verify(
                repository => repository.DeleteAsync(
                    WishlistId,
                    callerUserId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentFilters.Verify(
                repository => repository.DeleteAsync(
                    BackingFilterId,
                    callerUserId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestCaseSource(nameof(UndeletableFilters))]
    public void DeleteAsync_ThrowsAndKeepsTheContentFilter_WhenTheCallerCannotModifyIt(
        Guid? filterOwnerUserId,
        bool filterOwnerLock,
        Guid callerUserId,
        bool callerIsAdmin)
    {
        // Arrange
        _wishlists
            .Setup(repository => repository.GetByIdAsync(WishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = WishlistId,
                Name = "Wanted",
                OwnerUserId = OwnerId,
                ContentFilterId = BackingFilterId
            });
        _contentFilters
            .Setup(repository => repository.GetByIdAsync(BackingFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentFilter
            {
                Id = BackingFilterId,
                Name = "Protected",
                Criteria = new ContentFilterCriteria(),
                OwnerUserId = filterOwnerUserId,
                OwnerLock = filterOwnerLock
            });

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await _subject.DeleteAsync(WishlistId, true, callerUserId, callerIsAdmin)
                    .ConfigureAwait(false),
                Throws.TypeOf<MagpieException>().With.Message.EqualTo("The content filter could not be deleted."));
            _contentFilters.Verify(
                repository => repository.DeleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ListOwnAsync_ReadsEveryMatchingRow_WhenAPageIsShorterThanThePageSize()
    {
        // Arrange
        _wishlists
            .Setup(repository => repository.QueryAsync(
                It.IsAny<WishlistQuery>(),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Wishlist>
            {
                Items = [new Wishlist { Name = "first" }],
                PageNumber = 1,
                PageSize = 500,
                TotalItems = 501,
            });
        _wishlists
            .Setup(repository => repository.QueryAsync(
                It.IsAny<WishlistQuery>(),
                It.Is<PagingParameter>(paging => paging.PageNumber == 2),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Wishlist>
            {
                Items =
                [
                    new Wishlist { Name = "second" },
                    new Wishlist { Name = "third" },
                ],
                PageNumber = 2,
                PageSize = 500,
                TotalItems = 501,
            });

        // Act
        var items = await _subject.ListOwnAsync(OwnerId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items[0].Name, Is.EqualTo("first"));
            Assert.That(items[2].Name, Is.EqualTo("third"));
        }
    }

    [Test]
    public async Task ListOwnAsync_Stops_WhenAPageIsEmpty()
    {
        // Arrange
        _wishlists
            .Setup(repository => repository.QueryAsync(
                It.IsAny<WishlistQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Wishlist>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 500,
                TotalItems = 501,
            });

        // Act
        var items = await _subject.ListOwnAsync(OwnerId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Is.Empty);
            _wishlists.Verify(
                repository => repository.QueryAsync(
                    It.IsAny<WishlistQuery>(),
                    It.IsAny<PagingParameter>(),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestCaseSource(nameof(ReadableFilterCallers))]
    public async Task GetAsync_NestsTheContentFilter_WhenTheCallerCanReadIt(
        Guid? filterOwnerUserId,
        Guid callerUserId,
        bool callerIsAdmin)
    {
        // Arrange
        var filter = new ContentFilter
        {
            Id = ReferencedFilterId,
            Name = "Readable",
            Criteria = new ContentFilterCriteria(),
            OwnerUserId = filterOwnerUserId
        };
        _wishlists
            .Setup(repository => repository.GetByIdAsync(WishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = WishlistId,
                Name = "Wanted",
                OwnerUserId = OwnerId,
                ContentFilterId = ReferencedFilterId,
                ContentFilter = filter
            });
        _contentFilters
            .Setup(repository => repository.GetByIdAsync(ReferencedFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(filter);

        // Act
        var wishlist = await _subject.GetAsync(WishlistId, callerUserId, callerIsAdmin).ConfigureAwait(false);

        // Assert
        Assert.That(wishlist!.ContentFilter?.Id, Is.EqualTo(ReferencedFilterId));
    }

    [Test]
    public async Task GetAsync_HidesTheContentFilter_WhenTheCallerCannotReadIt()
    {
        // Arrange
        var filter = new ContentFilter
        {
            Id = ReferencedFilterId,
            Name = "Private",
            Criteria = new ContentFilterCriteria(),
            OwnerUserId = OtherUserId,
            OwnerLock = true
        };
        _wishlists
            .Setup(repository => repository.GetByIdAsync(WishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wishlist
            {
                Id = WishlistId,
                Name = "Wanted",
                OwnerUserId = OwnerId,
                ContentFilterId = ReferencedFilterId,
                ContentFilter = filter
            });
        _contentFilters
            .Setup(repository => repository.GetByIdAsync(ReferencedFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(filter);

        // Act
        var wishlist = await _subject.GetAsync(WishlistId, OwnerId, false).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wishlist!.ContentFilter, Is.Null);
            Assert.That(wishlist.ContentFilterId, Is.EqualTo(ReferencedFilterId));
        }
    }

    [Test]
    public async Task ListVisibleAsync_HidesTheContentFilter_WhenTheCallerCannotReadIt()
    {
        // Arrange
        var shared = new ContentFilter
        {
            Id = SharedFilterId,
            Name = "Everyone",
            Criteria = new ContentFilterCriteria(),
            OwnerUserId = null
        };
        _wishlists
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Wishlist
                {
                    Id = WishlistId,
                    Name = "Wanted",
                    OwnerUserId = OwnerId,
                    ContentFilterId = SharedFilterId,
                    ContentFilter = shared
                },
                new Wishlist
                {
                    Id = OtherWishlistId,
                    Name = "Also wanted",
                    OwnerUserId = OwnerId,
                    ContentFilterId = ReferencedFilterId,
                    ContentFilter = new ContentFilter
                    {
                        Id = ReferencedFilterId,
                        Name = "Private",
                        Criteria = new ContentFilterCriteria(),
                        OwnerUserId = OtherUserId
                    }
                }
            ]);
        _contentFilters
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentFilterQuery>(query => query.SharedOnly == true),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentFilter>
            {
                Items = [shared],
                PageNumber = 1,
                PageSize = 500,
                TotalItems = 1
            });

        // Act
        var items = await _subject.ListVisibleAsync(null, OwnerId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].ContentFilter?.Id, Is.EqualTo(SharedFilterId));
            Assert.That(items[1].ContentFilter, Is.Null);
        }
    }

    [Test]
    public async Task ListOwnAsync_HidesTheContentFilter_WhenTheCallerCannotReadIt()
    {
        // Arrange
        var own = new ContentFilter
        {
            Id = BackingFilterId,
            Name = "Wanted (wishlist)",
            Criteria = new ContentFilterCriteria(),
            OwnerUserId = OwnerId
        };
        _wishlists
            .Setup(repository => repository.QueryAsync(
                It.IsAny<WishlistQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Wishlist>
            {
                Items =
                [
                    new Wishlist
                    {
                        Id = WishlistId,
                        Name = "Wanted",
                        OwnerUserId = OwnerId,
                        ContentFilterId = BackingFilterId,
                        ContentFilter = own
                    },
                    new Wishlist
                    {
                        Id = OtherWishlistId,
                        Name = "Also wanted",
                        OwnerUserId = OwnerId,
                        ContentFilterId = ReferencedFilterId,
                        ContentFilter = new ContentFilter
                        {
                            Id = ReferencedFilterId,
                            Name = "Private",
                            Criteria = new ContentFilterCriteria(),
                            OwnerUserId = OtherUserId
                        }
                    }
                ],
                PageNumber = 1,
                PageSize = 500,
                TotalItems = 2
            });
        _contentFilters
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentFilterQuery>(query => query.OwnerUserId == OwnerId),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentFilter>
            {
                Items = [own],
                PageNumber = 1,
                PageSize = 500,
                TotalItems = 1
            });

        // Act
        var items = await _subject.ListOwnAsync(OwnerId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].ContentFilter?.Id, Is.EqualTo(BackingFilterId));
            Assert.That(items[1].ContentFilter, Is.Null);
        }
    }
}
