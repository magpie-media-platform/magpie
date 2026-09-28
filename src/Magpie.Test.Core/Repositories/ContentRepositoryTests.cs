using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ContentRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IContentRepository _subject = null!;
    private FakeTimeProvider _time = null!;
    private ContentHierarchyItem _hierarchyItem = null!;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        using var context = new MagpieDbContext(options.UseSqlite(_template).Options);
        context.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void DisposeTemplate()
    {
        _template.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _template.BackupDatabase(connection);
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        _context = new MagpieDbContext(options.UseSqlite(connection, contextOwnsConnection: true).Options);
        var metadata = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainer = metadata,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "Item A",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(_hierarchyItem);
        _context.SaveChanges();
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        _subject = new ContentRepository(
            _context,
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IEventBus>(),
            _time,
            NullLogger<ContentRepository>.Instance, Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IFileSystem>(), Mock.Of<IAuditRecordRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [TestCase(SortDirection.Ascending, 2020, 2021)]
    [TestCase(SortDirection.Descending, 2021, 2020)]
    public async Task QueryAsync_ReturnsSortedItems_WhenScalarSortIsRequested(
        SortDirection direction,
        int firstYear,
        int secondYear)
    {
        // Arrange
        _hierarchyItem.ContentItem = new()
        {
            CreatedAt = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        _context.ContentHierarchyItems.Add(new()
        {
            MetadataContainer = new(),
            ContentType = "test",
            Level = "item",
            Slug = "second",
            Name = "Second",
            ContentItem = new()
            {
                CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new(), new() { PageNumber = 1, PageSize = 10 },
            new() { PropertyName = "createdAt", Direction = direction }).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.CreatedAt.Year), Is.EqualTo([firstYear, secondYear]));
    }

    [TestCase(FulfillmentStatus.Fulfilled)]
    [TestCase(FulfillmentStatus.FulfilledExternally)]
    public async Task UpdateAsync_PreservesUnknownFulfilledAt_WhenItemRemainsFulfilled(FulfillmentStatus status)
    {
        // Arrange
        var item = new ContentItem
        {
            ContentHierarchyItemId = _hierarchyItem.Id,
            Fulfillment = status,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.UpdateAsync(item, null).ConfigureAwait(false);

        // Assert
        Assert.That((await _subject.GetByIdAsync(item.Id).ConfigureAwait(false))!.FulfilledAt, Is.Null);
    }

    [Test]
    public async Task UpdateAsync_KeepsUnknownDate_WhenStatusChangesWithoutMediaHistory()
    {
        // Arrange
        var item = new ContentItem
        {
            ContentHierarchyItemId = _hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        item.Fulfillment = FulfillmentStatus.Fulfilled;

        // Act
        await _subject.UpdateAsync(item, null).ConfigureAwait(false);

        // Assert
        Assert.That(
            (await _subject.GetByIdAsync(item.Id).ConfigureAwait(false))!.FulfilledAt,
            Is.Null);
    }

    [TestCase(FulfillmentStatus.Fulfilled, FulfillmentStatus.FulfilledExternally)]
    [TestCase(FulfillmentStatus.FulfilledExternally, FulfillmentStatus.Fulfilled)]
    [TestCase(FulfillmentStatus.Fulfilled, FulfillmentStatus.Upgradable)]
    [TestCase(FulfillmentStatus.Partial, FulfillmentStatus.Fulfilled)]
    [TestCase(FulfillmentStatus.Upgradable, FulfillmentStatus.Partial)]
    public async Task UpdateAsync_KeepsFulfilledAt_WhenFulfillmentStaysFulfilled(
        FulfillmentStatus from,
        FulfillmentStatus to)
    {
        // Arrange
        var item = new ContentItem
        {
            ContentHierarchyItemId = _hierarchyItem.Id,
            Fulfillment = from,
            FulfilledAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        item.Fulfillment = to;

        // Act
        await _subject.UpdateAsync(item, null).ConfigureAwait(false);

        // Assert
        Assert.That(
            (await _subject.GetByIdAsync(item.Id).ConfigureAwait(false))!.FulfilledAt,
            Is.EqualTo(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
    }

    [Test]
    public async Task UpdateAsync_ClearsFulfilledAt_WhenFulfillmentBecomesMissing()
    {
        // Arrange
        var item = new ContentItem
        {
            ContentHierarchyItemId = _hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            FulfilledAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        item.Fulfillment = FulfillmentStatus.Missing;

        // Act
        await _subject.UpdateAsync(item, null).ConfigureAwait(false);

        // Assert
        Assert.That(
            (await _subject.GetByIdAsync(item.Id).ConfigureAwait(false))!.FulfilledAt,
            Is.Null);
    }
}
