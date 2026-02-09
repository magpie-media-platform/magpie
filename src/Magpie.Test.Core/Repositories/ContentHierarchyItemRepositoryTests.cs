using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class ContentHierarchyItemRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IContentHierarchyItemRepository _subject = null!;
    private Guid _userId;
    private Guid _metadataContainerId;

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

        _context.Users.Add(user);
        _context.SaveChanges();

        _userId = user.Id;

        var metadataContainer = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };

        _context.MetadataContainers.Add(metadataContainer);
        _context.SaveChanges();

        _metadataContainerId = metadataContainer.Id;

        var loggerFactory = LoggerFactory.Create(builder => builder.AddTestLogger());

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());

        var transactionManager = new TransactionManager(_context);

        _subject = new ContentHierarchyItemRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<ContentHierarchyItemRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsContentHierarchyItem_WhenExists()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "test-movie",
            Name = "Test Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(item.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("Test Movie"));
            Assert.That(result, Has.Property(nameof(result.Slug)).EqualTo("test-movie"));
            Assert.That(result, Has.Property(nameof(result.ContentType)).EqualTo("movies"));
            Assert.That(result, Has.Property(nameof(result.Level)).EqualTo("movie"));
            Assert.That(result, Has.Property(nameof(result.Monitoring)).EqualTo(MonitoringIntent.Active));
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
    public async Task GetBySlugAsync_ReturnsContentHierarchyItem_WhenExists()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "test-movie-slug",
            Name = "Test Movie Slug",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetBySlugAsync("movies", "movie", "test-movie-slug");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("Test Movie Slug"));
            Assert.That(result, Has.Property(nameof(result.Slug)).EqualTo("test-movie-slug"));
            Assert.That(result, Has.Property(nameof(result.ContentType)).EqualTo("movies"));
            Assert.That(result, Has.Property(nameof(result.Level)).EqualTo("movie"));
        }
    }

    [Test]
    public async Task GetBySlugAsync_ReturnsNull_WhenNotExists()
    {
        // Act
        var result = await _subject.GetBySlugAsync("movies", "movie", "nonexistent-slug");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllItems()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync();

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "movie-1",
            Name = "Movie 1",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "movies",
            Level = "movie",
            Slug = "movie-2",
            Name = "Movie 2",
            Monitoring = MonitoringIntent.None,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoItems()
    {
        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesContentHierarchyItem()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "new-movie",
            Name = "New Movie",
            Monitoring = MonitoringIntent.Active,
            Path = "/movies/new-movie",
            LanguagePreferences = ["en", "es"]
        };

        // Act
        var result = await _subject.CreateAsync(item, _userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("New Movie"));
            Assert.That(result, Has.Property(nameof(result.Slug)).EqualTo("new-movie"));
            Assert.That(result, Has.Property(nameof(result.ContentType)).EqualTo("movies"));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "audited-movie",
            Name = "Audited Movie",
            Monitoring = MonitoringIntent.Active
        };

        // Act
        await _subject.CreateAsync(item, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType))
                .EqualTo("ContentHierarchyItem"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesContentHierarchyItem_WhenExists()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "original-movie",
            Name = "Original Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync();

        item.Name = "Updated Movie";
        item.Monitoring = MonitoringIntent.Excluded;
        item.Path = "/movies/updated-movie";

        // Act
        var result = await _subject.UpdateAsync(item, _userId);

        // Assert
        var updated = await _subject.GetByIdAsync(item.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated, Has.Property(nameof(updated.Name)).EqualTo("Updated Movie"));
            Assert.That(updated, Has.Property(nameof(updated.Monitoring)).EqualTo(MonitoringIntent.Excluded));
            Assert.That(updated, Has.Property(nameof(updated.Path)).EqualTo("/movies/updated-movie"));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "nonexistent-movie",
            Name = "Nonexistent Movie",
            Monitoring = MonitoringIntent.Active
        };

        // Act
        var result = await _subject.UpdateAsync(item, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "to-update-movie",
            Name = "To Update Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync();

        item.Name = "Updated";

        // Act
        await _subject.UpdateAsync(item, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType))
                .EqualTo("ContentHierarchyItem"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesContentHierarchyItem_WhenExists()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "to-delete-movie",
            Name = "To Delete Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(item.Id, _userId);

        // Assert
        var deleted = await _subject.GetByIdAsync(item.Id);
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
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "delete-audit-movie",
            Name = "Delete Audit Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync(item.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType))
                .EqualTo("ContentHierarchyItem"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByContentType()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync();

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "movie-1",
            Name = "Movie 1",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "tv",
            Level = "series",
            Slug = "series-1",
            Name = "Series 1",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync();

        var criteria = new ContentHierarchyItemCriteria
        {
            ContentType = new()
            {
                And = [new() { Value = "movies" }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items, Has.All.Matches<ContentHierarchyItem>(i => i.ContentType == "movies"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByLevel()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync();

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "tv",
            Level = "series",
            Slug = "series-1",
            Name = "Series 1",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync();

        var criteria = new ContentHierarchyItemCriteria
        {
            Level = new()
            {
                And = [new() { Value = "series" }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items, Has.All.Matches<ContentHierarchyItem>(i => i.Level == "series"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByMonitoring()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync();

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "active-movie",
            Name = "Active Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "movies",
            Level = "movie",
            Slug = "excluded-movie",
            Name = "Excluded Movie",
            Monitoring = MonitoringIntent.Excluded,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync();

        var criteria = new ContentHierarchyItemCriteria
        {
            Monitoring = new()
            {
                And = [new() { Value = MonitoringIntent.Active }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items, Has.All.Matches<ContentHierarchyItem>(i => i.Monitoring == MonitoringIntent.Active));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsPagedItems()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync();

            var item = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "movies",
                Level = "movie",
                Slug = $"movie-{i}",
                Name = $"Movie {i}",
                Monitoring = MonitoringIntent.Active,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
        }
        await _context.SaveChangesAsync();

        var paging = new PagingParameter { PageNumber = 1, PageSize = 2 };

        // Act
        var result = await _subject.QueryAsync(new(), paging, null);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(5));
            Assert.That(result, Has.Property(nameof(result.PageNumber)).EqualTo(1));
            Assert.That(result, Has.Property(nameof(result.PageSize)).EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsSortedItems()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(container2, container3);
        await _context.SaveChangesAsync();

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "movie-b",
            Name = "B Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "movies",
            Level = "movie",
            Slug = "movie-a",
            Name = "A Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        var item3 = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "movies",
            Level = "movie",
            Slug = "movie-c",
            Name = "C Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2, item3);
        await _context.SaveChangesAsync();

        var sort = new SortParameter { PropertyName = "Name", Direction = SortDirection.Ascending };

        // Act
        var result = await _subject.QueryAsync(new(), null, sort);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items, Has.ItemAt(0).Property(nameof(ContentHierarchyItem.Name)).EqualTo("A Movie"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(ContentHierarchyItem.Name)).EqualTo("B Movie"));
            Assert.That(items, Has.ItemAt(2).Property(nameof(ContentHierarchyItem.Name)).EqualTo("C Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByName()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync();

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "action-movie",
            Name = "Action Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "movies",
            Level = "movie",
            Slug = "comedy-movie",
            Name = "Comedy Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync();

        var criteria = new ContentHierarchyItemCriteria
        {
            Name = new()
            {
                And = [new()
                {
                    Value = "Action",
                    Operator = CriteriaValueOperator.ContainsString
                }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items, Has.One.With.Property(nameof(ContentHierarchyItem.Name)).EqualTo("Action Movie"));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesMonitoring()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "patch-movie",
            Name = "Patch Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId)
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync();

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Monitoring = new() { Value = MonitoringIntent.Excluded }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(item.Id);
        Assert.That(updated, Has.Property(nameof(updated.Monitoring)).EqualTo(MonitoringIntent.Excluded));
    }

    [Test]
    public async Task PatchAsync_UpdatesPath()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "path-movie",
            Name = "Path Movie",
            Monitoring = MonitoringIntent.Active,
            Path = "/old/path",
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId)
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync();

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Path = new() { Value = "/new/path" }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(item.Id);
        Assert.That(updated, Has.Property(nameof(updated.Path)).EqualTo("/new/path"));
    }

    [Test]
    public async Task PatchAsync_UpdatesLanguagePreferences()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "lang-movie",
            Name = "Language Movie",
            Monitoring = MonitoringIntent.Active,
            LanguagePreferences = ["en"],
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId)
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync();

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            LanguagePreferences = new()
            {
                Insert = new() { [1] = "es" }
            }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(item.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated!.LanguagePreferences, Has.Count.EqualTo(2));
            Assert.That(updated!.LanguagePreferences[0], Is.EqualTo("en"));
            Assert.That(updated!.LanguagePreferences[1], Is.EqualTo("es"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var patch = new ContentHierarchyItemPatch
        {
            Id = Guid.NewGuid(),
            Monitoring = new() { Value = MonitoringIntent.Excluded }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "audit-patch-movie",
            Name = "Audit Patch Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId)
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync();

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Monitoring = new() { Value = MonitoringIntent.Excluded }
        };

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType))
                .EqualTo("ContentHierarchyItem"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_AddsTags()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "tag-movie",
            Name = "Tag Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId)
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync();

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Tags = new() { Add = ["action", "adventure"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var tags = await _context.Tags.Include(t => t.MetadataContainers).ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tags, Has.Count.EqualTo(2));
            Assert.That(tags, Has.One.With.Property(nameof(Tag.Name)).EqualTo("action"));
            Assert.That(tags, Has.One.With.Property(nameof(Tag.Name)).EqualTo("adventure"));
        }
    }

    [Test]
    public async Task PatchAsync_RemovesTags()
    {
        // Arrange
        var metadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId);

        var tag = new Tag { Name = "to-remove" };
        tag.MetadataContainers.Add(metadataContainer!);
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();

        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "remove-tag-movie",
            Name = "Remove Tag Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = metadataContainer
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync();

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Tags = new() { Remove = ["to-remove"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updatedTag = await _context.Tags
            .Include(t => t.MetadataContainers)
            .FirstOrDefaultAsync(t => t.Name == "to-remove");
        Assert.That(updatedTag!.MetadataContainers, Has
            .None.Matches<MetadataContainer>(mc => mc.Id == _metadataContainerId));
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecordsForTagChanges()
    {
        // Arrange
        var metadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId);

        var existingTag = new Tag { Name = "existing-tag" };
        existingTag.MetadataContainers.Add(metadataContainer!);
        _context.Tags.Add(existingTag);
        await _context.SaveChangesAsync();

        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "tag-audit-movie",
            Name = "Tag Audit Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = metadataContainer
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync();

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Tags = new()
            {
                Add = ["new-tag"],
                Remove = ["existing-tag"]
            }
        };

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(3));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("AddTag"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("RemoveTag"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_BySlug()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync();

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "specific-slug",
            Name = "Specific Slug Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "movies",
            Level = "movie",
            Slug = "other-slug",
            Name = "Other Slug Movie",
            Monitoring = MonitoringIntent.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync();

        var criteria = new ContentHierarchyItemCriteria
        {
            Slug = new()
            {
                And = [new() { Value = "specific-slug" }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items, Has.One.With.Property(nameof(ContentHierarchyItem.Slug)).EqualTo("specific-slug"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByPath()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync();

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "movies",
            Level = "movie",
            Slug = "path-movie-1",
            Name = "Path Movie 1",
            Monitoring = MonitoringIntent.Active,
            Path = "/movies/action/test",
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "movies",
            Level = "movie",
            Slug = "path-movie-2",
            Name = "Path Movie 2",
            Monitoring = MonitoringIntent.Active,
            Path = "/movies/comedy/test",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync();

        var criteria = new ContentHierarchyItemCriteria
        {
            Path = new()
            {
                And = [new()
                {
                    Value = "action",
                    Operator = CriteriaValueOperator.ContainsString
                }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items, Has.One.With.Property(nameof(ContentHierarchyItem.Path)).Contains("action"));
        }
    }
}
