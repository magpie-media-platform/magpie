using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ContentHierarchyItemRepositoryDeleteTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IContentHierarchyItemRepository _subject = null!;
    private Guid _metadataContainerId;
    private Guid _userId;

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
        var user = new User
        {
            Username = "TestUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(user);
        _context.SaveChanges();
        _userId = user.Id;
        var metadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(metadataContainer);
        _context.SaveChanges();
        _metadataContainerId = metadataContainer.Id;
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var auditRecords = new AuditRecordRepository(
            _context, Mock.Of<IEventBus>(), NullLogger<AuditRecordRepository>.Instance,
            transactions, Mock.Of<IJobExecutionScopeFactory>(), TimeProvider.System);
        var subjects = new SubjectRepository(
            _context, auditRecords, transactions, Mock.Of<IEventBus>(),
            NullLogger<SubjectRepository>.Instance, TimeProvider.System);
        var metadataContainers = new MetadataContainerRepository(
            _context, auditRecords, subjects, transactions,
            NullLogger<MetadataContainerRepository>.Instance, TimeProvider.System);
        _subject = new ContentHierarchyItemRepository(
            _context, auditRecords, transactions,
            metadataContainers,
            Mock.Of<IContentTypeRegistry>(), Mock.Of<IEventBus>(),
            NullLogger<ContentHierarchyItemRepository>.Instance, TimeProvider.System, Mock.Of<IContentRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task DeleteAsync_DeletesContentHierarchyItem_WhenExists()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "to-delete-movie",
            Name = "To Delete Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(item.Id, _userId).ConfigureAwait(false);

        // Assert
        var deleted = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(deleted, Is.Null);
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord_WhenItemIsDeleted()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "delete-audit-movie",
            Name = "Delete Audit Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(item.Id, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(
                auditRecords,
                Has.One.With.Property(nameof(AuditRecord.EntityType))
                    .EqualTo("ContentHierarchyItem"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_RemovesSubtreeAndContentItems_WhenItemHasChildrenAndContentItems()
    {
        // Arrange
        var childContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(childContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var parent = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-f",
            Level = "level-b",
            Slug = "to-delete-series",
            Name = "To Delete Series",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(parent);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var child = new ContentHierarchyItem
        {
            MetadataContainerId = childContainer.Id,
            ParentId = parent.Id,
            ContentType = "type-f",
            Level = "level-c",
            Slug = "to-delete-season",
            Name = "To Delete Season",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(child);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var parentContentItem = new ContentItem
        {
            ContentHierarchyItemId = parent.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        var childContentItem = new ContentItem
        {
            ContentHierarchyItemId = child.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.AddRange(parentContentItem, childContentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(parent.Id, _userId).ConfigureAwait(false);

        // Assert
        var remainingHierarchyItems = await _context.ContentHierarchyItems.CountAsync().ConfigureAwait(false);
        var remainingContentItems = await _context.ContentItems.CountAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(remainingHierarchyItems, Is.Zero);
            Assert.That(remainingContentItems, Is.Zero);
        }
    }

    [Test]
    public async Task DeleteAsync_RemovesReleaseScope_WhenSubtreeHasCoverage()
    {
        // Arrange
        var childContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(childContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var parent = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-f",
            Level = "level-b",
            Slug = "coverage-delete-series",
            Name = "Coverage Delete Series",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(parent);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var child = new ContentHierarchyItem
        {
            MetadataContainerId = childContainer.Id,
            ParentId = parent.Id,
            ContentType = "type-f",
            Level = "level-c",
            Slug = "coverage-delete-season",
            Name = "Coverage Delete Season",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(child);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var release = new Release
        {
            Protocol = "torrent",
            Title = "Coverage Delete Pack",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.ReleaseScopes.Add(new ReleaseScope { ReleaseId = release.Id, ContentHierarchyItemId = child.Id, });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(parent.Id, _userId).ConfigureAwait(false);

        // Assert
        var remainingHierarchyItems = await _context.ContentHierarchyItems.CountAsync().ConfigureAwait(false);
        var remainingScopeRows = await _context.ReleaseScopes.CountAsync().ConfigureAwait(false);
        var releaseStillExists = await _context.Releases.AnyAsync(r => r.Id == release.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(remainingHierarchyItems, Is.Zero);
            Assert.That(remainingScopeRows, Is.Zero);
            Assert.That(releaseStillExists, Is.True);
        }
    }

    [Test]
    public async Task DeleteAsync_RemovesMetadataGraph_WhenTheDeletedSubtreeHasASibling()
    {
        // Arrange
        var deleteContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var siblingContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(deleteContainer, siblingContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var toDelete = new ContentHierarchyItem
        {
            MetadataContainerId = deleteContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "graph-delete",
            Name = "Graph Delete",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var sibling = new ContentHierarchyItem
        {
            MetadataContainerId = siblingContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "graph-keep",
            Name = "Graph Keep",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(toDelete, sibling);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var deleteContribution = new MetadataContribution
        {
            MetadataContainerId = deleteContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Graph Delete"
        };
        var siblingContribution = new MetadataContribution
        {
            MetadataContainerId = siblingContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Graph Keep"
        };
        _context.MetadataContributions.AddRange(deleteContribution, siblingContribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.Genres.AddRange(
            new Genre { MetadataContributionId = deleteContribution.Id, Name = "Drama", Ordinal = 0 },
            new Genre { MetadataContributionId = siblingContribution.Id, Name = "Comedy", Ordinal = 0 });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject.DeleteAsync(toDelete.Id, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(
                await _context.MetadataContainers.AsNoTracking().AnyAsync(c => c.Id == deleteContainer.Id)
                    .ConfigureAwait(false),
                Is.False);
            Assert.That(
                await _context.MetadataContributions.AsNoTracking()
                    .AnyAsync(c => c.MetadataContainerId == deleteContainer.Id).ConfigureAwait(false),
                Is.False);
            Assert.That(
                await _context.Genres.AsNoTracking()
                    .AnyAsync(g => g.MetadataContributionId == deleteContribution.Id).ConfigureAwait(false),
                Is.False);

            Assert.That(
                await _context.MetadataContainers.AsNoTracking().AnyAsync(c => c.Id == siblingContainer.Id)
                    .ConfigureAwait(false),
                Is.True);
            Assert.That(
                await _context.Genres.AsNoTracking()
                    .AnyAsync(g => g.MetadataContributionId == siblingContribution.Id).ConfigureAwait(false),
                Is.True);
        }
    }

    [Test]
    public async Task DeleteSubtreeAsync_ReportsNoDeletedIds_WhenTheItemDoesNotExist()
    {
        // Arrange

        // Act
        var deletedIds = await _subject.DeleteSubtreeAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        Assert.That(deletedIds, Is.Empty);
    }

    [Test]
    public async Task BulkDeleteAsync_RemovesMetadataGraphForSubtree_WhenTheDeletedSubtreeHasASibling()
    {
        // Arrange
        var showContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var seasonContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var siblingContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(showContainer, seasonContainer, siblingContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var show = new ContentHierarchyItem
        {
            MetadataContainerId = showContainer.Id,
            ContentType = "type-c",
            Level = "level-b",
            Slug = "bulk-show",
            Name = "Bulk Show",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(show);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var season = new ContentHierarchyItem
        {
            MetadataContainerId = seasonContainer.Id,
            ParentId = show.Id,
            ContentType = "type-c",
            Level = "level-c",
            Slug = "bulk-season",
            Name = "Bulk Season",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var sibling = new ContentHierarchyItem
        {
            MetadataContainerId = siblingContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "bulk-keep",
            Name = "Bulk Keep",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(season, sibling);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.MetadataContributions.AddRange(
            new MetadataContribution { MetadataContainerId = showContainer.Id, Ordinal = 0, ProviderType = "test" },
            new MetadataContribution { MetadataContainerId = seasonContainer.Id, Ordinal = 0, ProviderType = "test" },
            new MetadataContribution { MetadataContainerId = siblingContainer.Id, Ordinal = 0, ProviderType = "test" });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var (result, _) = await _subject.BulkDeleteAsync(new() { Ids = [show.Id] }, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(
                await _context.MetadataContainers.AsNoTracking().AnyAsync(c => c.Id == showContainer.Id)
                    .ConfigureAwait(false),
                Is.False);
            Assert.That(
                await _context.MetadataContainers.AsNoTracking().AnyAsync(c => c.Id == seasonContainer.Id)
                    .ConfigureAwait(false),
                Is.False);
            Assert.That(
                await _context.MetadataContributions.AsNoTracking()
                    .AnyAsync(c => c.MetadataContainerId == showContainer.Id ||
                        c.MetadataContainerId == seasonContainer.Id).ConfigureAwait(false),
                Is.False);
            Assert.That(
                await _context.MetadataContainers.AsNoTracking().AnyAsync(c => c.Id == siblingContainer.Id)
                    .ConfigureAwait(false),
                Is.True);
            Assert.That(
                await _context.MetadataContributions.AsNoTracking()
                    .AnyAsync(c => c.MetadataContainerId == siblingContainer.Id).ConfigureAwait(false),
                Is.True);
        }
    }

    [Test]
    public async Task DeleteAsync_RemovesMediaFiles_WhenContentItemHasFiles()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-with-files",
            Name = "Movie With Files",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = item.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.MediaFiles.AddRange(
            new MediaFile
            {
                ContentItemId = contentItem.Id,
                FileKind = "Video",
                FilePath = "/movies/movie-with-files/movie.mkv",
                PathKey = "/movies/movie-with-files/movie.mkv",
                FileSize = 1024,
                CreatedAt = DateTime.UtcNow
            },
            new MediaFile
            {
                ContentItemId = contentItem.Id,
                FileKind = "Subtitle",
                FilePath = "/movies/movie-with-files/movie.en.srt",
                PathKey = "/movies/movie-with-files/movie.en.srt",
                FileSize = 16,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(item.Id, _userId).ConfigureAwait(false);

        // Assert
        var remainingHierarchyItems = await _context.ContentHierarchyItems.CountAsync().ConfigureAwait(false);
        var remainingContentItems = await _context.ContentItems.CountAsync().ConfigureAwait(false);
        var remainingMediaFiles = await _context.MediaFiles.CountAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(remainingHierarchyItems, Is.Zero);
            Assert.That(remainingContentItems, Is.Zero);
            Assert.That(remainingMediaFiles, Is.Zero);
        }
    }

    [Test]
    public async Task DeleteAsync_RemovesSubtreeMediaFiles_WhenChildContentItemHasFiles()
    {
        // Arrange
        var childContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(childContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var parent = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-f",
            Level = "level-b",
            Slug = "series-with-file-child",
            Name = "Series With File Child",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(parent);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var child = new ContentHierarchyItem
        {
            MetadataContainerId = childContainer.Id,
            ParentId = parent.Id,
            ContentType = "type-f",
            Level = "level-c",
            Slug = "season-with-files",
            Name = "Season With Files",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(child);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var childContentItem = new ContentItem
        {
            ContentHierarchyItemId = child.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(childContentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.MediaFiles.Add(new MediaFile
        {
            ContentItemId = childContentItem.Id,
            FileKind = "Video",
            FilePath = "/episodes/season-with-files/s01e01.mkv",
            PathKey = "/episodes/season-with-files/s01e01.mkv",
            FileSize = 2048,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(parent.Id, _userId).ConfigureAwait(false);

        // Assert
        var remainingHierarchyItems = await _context.ContentHierarchyItems.CountAsync().ConfigureAwait(false);
        var remainingContentItems = await _context.ContentItems.CountAsync().ConfigureAwait(false);
        var remainingMediaFiles = await _context.MediaFiles.CountAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(remainingHierarchyItems, Is.Zero);
            Assert.That(remainingContentItems, Is.Zero);
            Assert.That(remainingMediaFiles, Is.Zero);
        }
    }

    [Test]
    public async Task BulkDeleteAsync_DeletesMatching_WhenSomeMatchesAreExcluded()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var a = new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "movie-a",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(a);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var b = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-b",
            Name = "movie-b",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(b);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var c = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-c",
            Name = "movie-c",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(c);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            ContentType = new() { And = [new() { Value = "type-a" }] },
            ExcludeIds = [c.Id]
        };

        // Act
        var (result, _) = await _subject.BulkDeleteAsync(new() { Filter = criteria }, _userId)
            .ConfigureAwait(false);

        // Assert
        var remaining = await _context.ContentHierarchyItems.AsNoTracking().Select(i => i.Id).ToListAsync()
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(remaining, Does.Not.Contain(a.Id));
            Assert.That(remaining, Does.Not.Contain(b.Id));
            Assert.That(remaining, Does.Contain(c.Id));
        }
    }

    [Test]
    public async Task DeleteAsync_ClearsDownloadScopes_WhenSubtreeIsScoped()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var seasonContainer = new MetadataContainer { CreatedAt = now };
        var episodeContainer = new MetadataContainer { CreatedAt = now };
        _context.MetadataContainers.AddRange(seasonContainer, episodeContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var season = new ContentHierarchyItem
        {
            MetadataContainerId = seasonContainer.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "s1",
            Name = "Season 1",
            CreatedAt = now
        };
        _context.ContentHierarchyItems.Add(season);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var episode = new ContentHierarchyItem
        {
            MetadataContainerId = episodeContainer.Id,
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "s1e1",
            Name = "Episode 1",
            CreatedAt = now
        };
        _context.ContentHierarchyItems.Add(episode);
        var client = new DownloadClient
        {
            Name = "client",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = now
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var download = new Download
        {
            DownloadClientId = client.Id,
            ExternalId = string.Empty,
            Title = "season pack",
            Status = DownloadStatus.Downloading,
            CreatedAt = now
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.Add(new DownloadScope
        {
            DownloadId = download.Id,
            ContentHierarchyItemId = episode.Id
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var deleted = await _subject.DeleteAsync(season.Id, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            Assert.That(
                await _context.DownloadScopes.AnyAsync(c => c.ContentHierarchyItemId == episode.Id)
                    .ConfigureAwait(false),
                Is.False);
        }
    }

    [TestCase("episode-1", "episode-2", new[] { "episode-3" })]
    [TestCase("episode-2", "episode-1", new[] { "episode-3" })]
    [TestCase("episode-3", "episode-1", new[] { "episode-2" })]
    public async Task DeleteAsync_KeepsTheFileForTheOtherEpisodes_WhenAnEpisodeThatTheFileCoversIsDeleted(
        string deletedSlug,
        string ownerSlug,
        string[] coveredSlugs)
    {
        // Arrange
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = _metadataContainerId,
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            CreatedAt = DateTime.UtcNow
        };
        var firstEpisode = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-1",
            Name = "Episode 1",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { CreatedAt = DateTime.UtcNow }
        };
        var secondEpisode = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-2",
            Name = "Episode 2",
            Ordinal = 2,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { CreatedAt = DateTime.UtcNow }
        };
        var thirdEpisode = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-3",
            Name = "Episode 3",
            Ordinal = 3,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { CreatedAt = DateTime.UtcNow }
        };
        _context.ContentHierarchyItems.AddRange(season, firstEpisode, secondEpisode, thirdEpisode);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var mediaFile = new MediaFile
        {
            ContentItemId = firstEpisode.ContentItem.Id,
            FileKind = "Video",
            FilePath = "/tv/S01E01-E03.mkv",
            PathKey = "/tv/S01E01-E03.mkv",
            CreatedAt = DateTime.UtcNow
        };
        mediaFile.Coverage.Add(new() { ContentItemId = secondEpisode.ContentItem.Id });
        mediaFile.Coverage.Add(new() { ContentItemId = thirdEpisode.ContentItem.Id });
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var deletedId = await _context.ContentHierarchyItems
            .Where(item => item.Slug == deletedSlug)
            .Select(item => item.Id)
            .SingleAsync()
            .ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(deletedId, _userId).ConfigureAwait(false);

        // Assert
        var slugs = await _context.ContentItems.AsNoTracking()
            .Join(
                _context.ContentHierarchyItems,
                item => item.ContentHierarchyItemId,
                node => node.Id,
                (item, node) => new { item.Id, node.Slug })
            .ToDictionaryAsync(item => item.Id, item => item.Slug)
            .ConfigureAwait(false);
        var stored = await _context.MediaFiles.AsNoTracking()
            .Include(file => file.Coverage)
            .SingleAsync()
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(slugs[stored.ContentItemId], Is.EqualTo(ownerSlug));
            Assert.That(stored.Coverage.Select(coverage => slugs[coverage.ContentItemId]), Is.EqualTo(coveredSlugs));
        }
    }
}
