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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

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
            NullLogger<ReleaseScopeRepository>.Instance);
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
}
