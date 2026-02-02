using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Models.Sorting;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class ReleaseRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IReleaseRepository _subject = null!;
    private Guid _userId;
    private Guid _indexerId;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var user = new User
        {
            Username = "TestUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow
        };

        var indexer = new Indexer
        {
            Name = "TestIndexer",
            ProviderType = "torznab",
            Availability = IndexerAvailability.Public,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.Indexers.Add(indexer);
        _context.SaveChanges();

        _userId = user.Id;
        _indexerId = indexer.Id;

        var loggerFactory = LoggerFactory.Create(builder => builder.AddTestLogger());

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());

        var transactionManager = new TransactionManager(_context);

        _subject = new ReleaseRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<ReleaseRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsRelease_WhenExists()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "TestRelease",
            PublishDate = DateTime.UtcNow,
            Size = 1024,
            Categories = ["movies"],
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(release.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("TestRelease"));
            Assert.That(result, Has.Property(nameof(result.Protocol)).EqualTo("torrent"));
            Assert.That(result, Has.Property(nameof(result.Size)).EqualTo(1024));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllReleases()
    {
        // Arrange
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release1",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "usenet",
            Title = "Release2",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoReleases()
    {
        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task QueryAsync_FiltersByIndexerId()
    {
        // Arrange
        var otherIndexer = new Indexer
        {
            Name = "OtherIndexer",
            ProviderType = "newznab",
            Availability = IndexerAvailability.Private,
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.Add(otherIndexer);
        await _context.SaveChangesAsync();

        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release1",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release2",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release3 = new Release
        {
            IndexerId = otherIndexer.Id,
            Protocol = "usenet",
            Title = "Release3",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2, release3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new ReleaseQuery(IndexerId: _indexerId),
            null,
            null,
            default);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByTitle()
    {
        // Arrange
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "TestMovie.2024.1080p",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "TestMovie.2024.720p",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release3 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "OtherShow.S01E01",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2, release3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new ReleaseQuery(Title: "TestMovie"),
            null,
            null,
            default);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(Release.Title)).Contains("TestMovie"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByCategory()
    {
        // Arrange
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release1",
            PublishDate = DateTime.UtcNow,
            Categories = ["movies", "hd"],
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release2",
            PublishDate = DateTime.UtcNow,
            Categories = ["tv", "hd"],
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new ReleaseQuery(Category: "movies"),
            null,
            null,
            default);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items, Has.One.With.Property(nameof(Release.Title)).EqualTo("Release1"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByExternalId()
    {
        // Arrange
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release1",
            ExternalId = "ext-123",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release2",
            ExternalId = "ext-456",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new ReleaseQuery(ExternalId: "ext-123"),
            null,
            null,
            default);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items, Has.One.With.Property(nameof(Release.ExternalId)).EqualTo("ext-123"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByPublishDateRange()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release1",
            PublishDate = now.AddDays(-5),
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release2",
            PublishDate = now.AddDays(-2),
            CreatedAt = DateTime.UtcNow
        };
        var release3 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release3",
            PublishDate = now.AddDays(-10),
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2, release3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new ReleaseQuery(PublishDate: new Range<DateTime>(now.AddDays(-6), now.AddDays(-1))),
            null,
            null,
            default);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByContentItemId()
    {
        // Arrange
        var metadataContainer1 = new MetadataContainer();
        var metadataContainer2 = new MetadataContainer();
        _context.MetadataContainers.AddRange(metadataContainer1, metadataContainer2);
        await _context.SaveChangesAsync();

        var hierarchyItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = metadataContainer1.Id,
            ContentType = "Movies",
            Level = "Movie",
            Slug = "test-movie-1",
            Name = "Test Movie 1",
            CreatedAt = DateTime.UtcNow
        };
        var hierarchyItem2 = new ContentHierarchyItem
        {
            MetadataContainerId = metadataContainer2.Id,
            ContentType = "Movies",
            Level = "Movie",
            Slug = "test-movie-2",
            Name = "Test Movie 2",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(hierarchyItem1, hierarchyItem2);
        await _context.SaveChangesAsync();

        var contentItem1 = new ContentItem
        {
            ContentHierarchyItemId = hierarchyItem1.Id,
            CreatedAt = DateTime.UtcNow
        };
        var contentItem2 = new ContentItem
        {
            ContentHierarchyItemId = hierarchyItem2.Id,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.AddRange(contentItem1, contentItem2);
        await _context.SaveChangesAsync();

        var release1 = new Release
        {
            IndexerId = _indexerId,
            ContentItemId = contentItem1.Id,
            Protocol = "torrent",
            Title = "Release1",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            ContentItemId = contentItem2.Id,
            Protocol = "torrent",
            Title = "Release2",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new ReleaseQuery(ContentItemId: contentItem1.Id),
            null,
            null,
            default);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items, Has.One.With.Property(nameof(Release.ContentItemId)).EqualTo(contentItem1.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByTags()
    {
        // Arrange
        var tag = new Tag { Name = "high-quality" };
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();

        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release1",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release2",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2);
        await _context.SaveChangesAsync();

        tag.Releases.Add(release1);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new ReleaseQuery(Tags: ["high-quality"]),
            null,
            null,
            default);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items, Has.One.With.Property(nameof(Release.Title)).EqualTo("Release1"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release1",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new ReleaseQuery(Title: "NonExistent"),
            null,
            null,
            default);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Empty);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(0));
        }
    }

    [Test]
    public async Task QueryAsync_AppliesPaging()
    {
        // Arrange
        for (var i = 0; i < 10; i++)
        {
            _context.Releases.Add(new Release
            {
                IndexerId = _indexerId,
                Protocol = "torrent",
                Title = $"Release{i}",
                PublishDate = DateTime.UtcNow.AddMinutes(-i),
                CreatedAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new ReleaseQuery(),
            new PagingParameter(2, 3),
            null,
            default);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(3).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(10));
            Assert.That(result, Has.Property(nameof(result.PageNumber)).EqualTo(2));
            Assert.That(result, Has.Property(nameof(result.PageSize)).EqualTo(3));
        }
    }

    [Test]
    public async Task QueryAsync_AppliesSortingAscending()
    {
        // Arrange
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Zebra",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Alpha",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release3 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Beta",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2, release3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new ReleaseQuery(),
            null,
            new SortParameter(nameof(Release.Title), SortDirection.Ascending),
            default);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items[0], Has.Property(nameof(Release.Title)).EqualTo("Alpha"));
            Assert.That(items[1], Has.Property(nameof(Release.Title)).EqualTo("Beta"));
            Assert.That(items[2], Has.Property(nameof(Release.Title)).EqualTo("Zebra"));
        }
    }

    [Test]
    public async Task QueryAsync_AppliesSortingDescending()
    {
        // Arrange
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Alpha",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Zebra",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new ReleaseQuery(),
            null,
            new SortParameter(nameof(Release.Title), SortDirection.Descending),
            default);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items[0], Has.Property(nameof(Release.Title)).EqualTo("Zebra"));
            Assert.That(items[1], Has.Property(nameof(Release.Title)).EqualTo("Alpha"));
        }
    }

    [Test]
    public async Task QueryAsync_DefaultSortsByPublishDateDescending()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "OldRelease",
            PublishDate = now.AddDays(-5),
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "NewRelease",
            PublishDate = now,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new ReleaseQuery(),
            null,
            null,
            default);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items[0], Has.Property(nameof(Release.Title)).EqualTo("NewRelease"));
            Assert.That(items[1], Has.Property(nameof(Release.Title)).EqualTo("OldRelease"));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesRelease()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "NewRelease",
            PublishDate = DateTime.UtcNow,
            Size = 2048,
            Categories = ["movies"],
            Languages = ["en"]
        };

        // Act
        var result = await _subject.CreateAsync(release, _userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("NewRelease"));
            Assert.That(result, Has.Property(nameof(result.Protocol)).EqualTo("torrent"));
            Assert.That(result, Has.Property(nameof(result.Size)).EqualTo(2048));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesReleaseWithAllFields()
    {
        // Arrange
        var publishDate = DateTime.UtcNow;
        var infoUrl = new Uri("https://example.com/release/123");

        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "usenet",
            ExternalId = "ext-release-123",
            Title = "CompleteRelease",
            PublishDate = publishDate,
            Size = 4096,
            InfoUrl = infoUrl,
            Categories = ["movies", "hd"],
            Languages = ["en", "fr"],
            Properties = "{\"quality\":\"1080p\"}"
        };

        // Act
        var result = await _subject.CreateAsync(release, _userId);

        // Assert
        var persisted = await _context.Releases
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == result.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted, Has.Property(nameof(persisted.ExternalId)).EqualTo("ext-release-123"));
            Assert.That(persisted, Has.Property(nameof(persisted.InfoUrl)).EqualTo(infoUrl));
            Assert.That(persisted, Has.Property(nameof(persisted.Properties)).EqualTo("{\"quality\":\"1080p\"}"));
            Assert.That(persisted, Has.Property(nameof(persisted.Categories)).EquivalentTo(new[] { "movies", "hd" }));
            Assert.That(persisted, Has.Property(nameof(persisted.Languages)).EquivalentTo(new[] { "en", "fr" }));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "AuditedRelease",
            PublishDate = DateTime.UtcNow
        };

        // Act
        await _subject.CreateAsync(release, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Release"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesRelease_WhenExists()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "OriginalTitle",
            PublishDate = DateTime.UtcNow,
            Size = 1024,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync();

        release.Title = "UpdatedTitle";
        release.Protocol = "usenet";
        release.Size = 2048;

        // Act
        var result = await _subject.UpdateAsync(release, _userId);

        // Assert
        var updated = await _subject.GetByIdAsync(release.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated, Has.Property(nameof(updated.Title)).EqualTo("UpdatedTitle"));
            Assert.That(updated, Has.Property(nameof(updated.Protocol)).EqualTo("usenet"));
            Assert.That(updated, Has.Property(nameof(updated.Size)).EqualTo(2048));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var release = new Release
        {
            Id = Guid.NewGuid(),
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "NonExistent",
            PublishDate = DateTime.UtcNow
        };

        // Act
        var result = await _subject.UpdateAsync(release, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "ToUpdate",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync();

        release.Title = "Updated";

        // Act
        await _subject.UpdateAsync(release, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Release"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesRelease_WhenExists()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "ToDelete",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(release.Id, _userId);

        // Assert
        var deleted = await _subject.GetByIdAsync(release.Id);
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
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "DeleteAudit",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync(release.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Release"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }
}
