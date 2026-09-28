using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class DownloadScopeRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IDownloadScopeRepository _subject = null!;
    private Guid _downloadClientId;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        using var context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(_template)
            .Options);
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
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .Options);

        var downloadClient = new DownloadClient
        {
            Name = "TestClient",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(downloadClient);
        _context.SaveChanges();
        _downloadClientId = downloadClient.Id;

        _subject = new DownloadScopeRepository(
            _context,
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IAuditRecordRepository>(),
            NullLogger<DownloadScopeRepository>.Instance,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [TestCase(DownloadStatus.Downloading, DownloadStatus.Downloading)]
    [TestCase(DownloadStatus.Importing, DownloadStatus.Importing)]
    [TestCase(DownloadStatus.Imported, null)]
    [TestCase(DownloadStatus.Failed, null)]
    [TestCase(DownloadStatus.Faulted, null)]
    [TestCase(DownloadStatus.Cancelled, null)]
    public async Task CreateAsync_RecomputesActiveAcquisitionStatus_WhenTheScopeIsCreated(
        DownloadStatus status,
        DownloadStatus? expected)
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainer = new(),
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "Item A",
            ContentItem = new()
        };
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            Title = "Scoped",
            ExternalId = "scoped",
            Status = status
        };
        _context.AddRange(item, download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.CreateAsync(
            new() { DownloadId = download.Id, ContentHierarchyItemId = item.Id }, null).ConfigureAwait(false);

        // Assert
        var stored = await _context.ContentItems.AsNoTracking()
            .Where(contentItem => contentItem.ContentHierarchyItemId == item.Id)
            .Select(contentItem => contentItem.ActiveAcquisitionStatus)
            .SingleAsync().ConfigureAwait(false);
        Assert.That(stored, Is.EqualTo(expected));
    }

    [TestCase(DownloadStatus.Downloading, DownloadStatus.Downloading)]
    [TestCase(DownloadStatus.Importing, DownloadStatus.Importing)]
    [TestCase(DownloadStatus.Imported, null)]
    [TestCase(DownloadStatus.Failed, null)]
    [TestCase(DownloadStatus.Faulted, null)]
    [TestCase(DownloadStatus.Cancelled, null)]
    public async Task UpdateAsync_MovesActiveAcquisitionStatus_WhenTheScopeMovesToAnotherItem(
        DownloadStatus status,
        DownloadStatus? expected)
    {
        // Arrange
        var first = new ContentHierarchyItem
        {
            MetadataContainer = new(),
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "Item A",
            ContentItem = new()
        };
        var second = new ContentHierarchyItem
        {
            MetadataContainer = new(),
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-b",
            Name = "Item B",
            ContentItem = new()
        };
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            Title = "Scoped",
            ExternalId = "scoped",
            Status = status
        };
        _context.AddRange(first, second, download);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var scope = await _subject.CreateAsync(
            new() { DownloadId = download.Id, ContentHierarchyItemId = first.Id }, null).ConfigureAwait(false);

        // Act
        await _subject.UpdateAsync(scope with { ContentHierarchyItemId = second.Id }, null).ConfigureAwait(false);

        // Assert
        var firstStatus = await _context.ContentItems.AsNoTracking()
            .Where(contentItem => contentItem.ContentHierarchyItemId == first.Id)
            .Select(contentItem => contentItem.ActiveAcquisitionStatus)
            .SingleAsync().ConfigureAwait(false);
        var secondStatus = await _context.ContentItems.AsNoTracking()
            .Where(contentItem => contentItem.ContentHierarchyItemId == second.Id)
            .Select(contentItem => contentItem.ActiveAcquisitionStatus)
            .SingleAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstStatus, Is.Null);
            Assert.That(secondStatus, Is.EqualTo(expected));
        }
    }

    [Test]
    public async Task DeleteAsync_ClearsActiveAcquisitionStatus_WhenTheScopeIsDeleted()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainer = new(),
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "Item A",
            ContentItem = new()
        };
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            Title = "Scoped",
            ExternalId = "scoped",
            Status = DownloadStatus.Downloading
        };
        _context.AddRange(item, download);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var scope = await _subject.CreateAsync(
            new() { DownloadId = download.Id, ContentHierarchyItemId = item.Id }, null).ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(scope.Id, null).ConfigureAwait(false);

        // Assert
        var stored = await _context.ContentItems.AsNoTracking()
            .Where(contentItem => contentItem.ContentHierarchyItemId == item.Id)
            .Select(contentItem => contentItem.ActiveAcquisitionStatus)
            .SingleAsync().ConfigureAwait(false);
        Assert.That(stored, Is.Null);
    }

    [Test]
    public async Task QueryAsync_SortsDescending_WhenSortIsRequested()
    {
        // Arrange
        var low = Guid.Parse("f5164031-ccc3-4aab-8920-de4a83262c7d");
        var high = Guid.Parse("f7a7f682-44f6-418f-9d25-86510b6350c6");
        var node = new ContentHierarchyItem
        {
            MetadataContainer = new(),
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "Item A"
        };
        _context.ContentHierarchyItems.Add(node);
        _context.Downloads.AddRange(
            new Download { Id = low, DownloadClientId = _downloadClientId, Title = "First", ExternalId = "first" },
            new Download { Id = high, DownloadClientId = _downloadClientId, Title = "Second", ExternalId = "second" });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.AddRange(
            new DownloadScope { DownloadId = low, ContentHierarchyItemId = node.Id },
            new DownloadScope { DownloadId = high, ContentHierarchyItemId = node.Id });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page = await _subject.QueryAsync(new(), new() { PageNumber = 1, PageSize = 10 },
            new() { PropertyName = "downloadId", Direction = SortDirection.Descending })
            .ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Select(scope => scope.DownloadId), Is.EqualTo([high, low]));
    }
}
