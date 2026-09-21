using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ReleaseScopeRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IReleaseScopeRepository _subject = null!;
    private Guid _releaseId;
    private Guid _hierarchyItemId;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainer = container,
            ContentType = "type-a",
            Level = "volume",
            Slug = "the-matrix",
            Name = "The Matrix",
            CreatedAt = DateTime.UtcNow
        };
        var release = new Release
        {
            Protocol = "torrent",
            ExternalId = "shared-row",
            Title = "The Matrix 1080p",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.AddRange(container, hierarchyItem, release);
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        _hierarchyItemId = hierarchyItem.Id;
        _releaseId = release.Id;
        ITransactionManager transactionManager = new TransactionManager(
            _context,
            NullLogger<TransactionManager>.Instance);
        _subject = new ReleaseScopeRepository(
            _context,
            transactionManager,
            Mock.Of<IAuditRecordRepository>(),
            NullLogger<ReleaseScopeRepository>.Instance,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingScope_WhenQueryMatches()
    {
        // Arrange
        var scope = await _subject.CreateAsync(
            new ReleaseScope { ReleaseId = _releaseId, ContentHierarchyItemId = _hierarchyItemId },
            null).ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new ReleaseScopeQuery { ReleaseId = _releaseId },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Id, Is.EqualTo(scope.Id));
            Assert.That(items[0].ContentHierarchyItemId, Is.EqualTo(_hierarchyItemId));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesScope_WhenScopeExists()
    {
        // Arrange
        var scope = await _subject.CreateAsync(
            new ReleaseScope { ReleaseId = _releaseId, ContentHierarchyItemId = _hierarchyItemId },
            null).ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteAsync(scope.Id, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            Assert.That(await _context.ReleaseScopes.CountAsync().ConfigureAwait(false), Is.Zero);
        }
    }

    [Test]
    public async Task QueryAsync_SortsDescending_WhenSortIsRequested()
    {
        // Arrange
        var low = Guid.Parse("eb564bcd-55cb-4f6b-9a29-f7a3c21a4ba9");
        var high = Guid.Parse("f3389f5f-fd45-4dbf-88df-1252cc1443f3");
        _context.Releases.AddRange(
            new Release { Id = low, Title = "First", Protocol = "torrent" },
            new Release { Id = high, Title = "Second", Protocol = "torrent" });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ReleaseScopes.AddRange(
            new ReleaseScope { ReleaseId = low, ContentHierarchyItemId = _hierarchyItemId },
            new ReleaseScope { ReleaseId = high, ContentHierarchyItemId = _hierarchyItemId });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page = await _subject.QueryAsync(new(), new() { PageNumber = 1, PageSize = 10 },
            new() { PropertyName = "releaseId", Direction = SortDirection.Descending })
            .ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Select(scope => scope.ReleaseId), Is.EqualTo([high, low]));
    }
}
