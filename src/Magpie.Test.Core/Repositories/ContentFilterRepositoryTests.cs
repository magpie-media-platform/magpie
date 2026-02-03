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
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class ContentFilterRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IContentFilterRepository _subject = null!;
    private Guid _userId;

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

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());
        var transactionManager = new TransactionManager(_context);

        _subject = new ContentFilterRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<ContentFilterRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    private static ContentFilterCriteria CreateTestCriteria()
    {
        return new ContentFilterCriteria();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsContentFilter()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "TestFilter",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestFilter"));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllContentFilters()
    {
        // Arrange
        var entity1 = new ContentFilter
        {
            Name = "Filter1",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        var entity2 = new ContentFilter
        {
            Name = "Filter2",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.AddRange(entity1, entity2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoContentFilters()
    {
        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetByNameAsync_ReturnsContentFilter()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "UniqueFilterName",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByNameAsync("UniqueFilterName");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("UniqueFilterName"));
        }
    }

    [Test]
    public async Task GetByNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByNameAsync("NonExistentFilter");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListByTagAsync_ReturnsContentFilters()
    {
        // Arrange
        var sharedTag = new Tag { Name = "shared" };
        var uniqueTag = new Tag { Name = "unique" };
        _context.Tags.AddRange(sharedTag, uniqueTag);

        var entity1 = new ContentFilter
        {
            Name = "Filter1",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        var entity2 = new ContentFilter
        {
            Name = "Filter2",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        var entity3 = new ContentFilter
        {
            Name = "Filter3",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        sharedTag.ContentFilters.Add(entity1);
        sharedTag.ContentFilters.Add(entity2);
        uniqueTag.ContentFilters.Add(entity3);
        _context.ContentFilters.AddRange(entity1, entity2, entity3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListByTagAsync("shared");

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListByTagAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.ListByTagAsync("nonexistent");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListByUserIdAsync_ReturnsContentFiltersForUser()
    {
        // Arrange
        var entity1 = new ContentFilter
        {
            Name = "UserFilter1",
            Criteria = CreateTestCriteria(),
            OwnerUserId = _userId,
            CreatedAt = DateTime.UtcNow
        };
        var entity2 = new ContentFilter
        {
            Name = "UserFilter2",
            Criteria = CreateTestCriteria(),
            OwnerUserId = _userId,
            CreatedAt = DateTime.UtcNow
        };
        var entity3 = new ContentFilter
        {
            Name = "OtherFilter",
            Criteria = CreateTestCriteria(),
            OwnerUserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.AddRange(entity1, entity2, entity3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListByUserIdAsync(_userId);

        // Assert
        var filters = result.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filters, Has.Count.EqualTo(2));
            Assert.That(filters, Has.One.Property(nameof(ContentFilter.Name)).EqualTo("UserFilter1"));
            Assert.That(filters, Has.One.Property(nameof(ContentFilter.Name)).EqualTo("UserFilter2"));
        }
    }

    [Test]
    public async Task ListByUserIdAsync_ReturnsEmptyList_WhenNoFiltersForUser()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "OtherFilter",
            Criteria = CreateTestCriteria(),
            OwnerUserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListByUserIdAsync(_userId);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesContentFilter()
    {
        // Arrange
        var model = new ContentFilter
        {
            Name = "NewFilter",
            Criteria = CreateTestCriteria()
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await _context.ContentFilters
            .AsNoTracking()
            .Where(f => f.Name == "NewFilter")
            .SingleOrDefaultAsync();
        var auditRecords = await _context.AuditRecords.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewFilter"));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity, Has.Property(nameof(entity.Name)).EqualTo("NewFilter"));
            Assert.That(entity, Has.Property(nameof(entity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(auditRecords, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task CreateAsync_WithNullUserId_CreatesContentFilter()
    {
        // Arrange
        var model = new ContentFilter
        {
            Name = "SystemFilter",
            Criteria = CreateTestCriteria()
        };

        // Act
        var result = await _subject.CreateAsync(model, null);

        // Assert
        var entity = await _context.ContentFilters
            .AsNoTracking()
            .Where(f => f.Name == "SystemFilter")
            .SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("SystemFilter"));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity, Has.Property(nameof(entity.CreatedByUserId)).EqualTo(null));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesContentFilter()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "OriginalName",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync();

        var model = new ContentFilter
        {
            Id = entity.Id,
            Name = "UpdatedName",
            Criteria = CreateTestCriteria(),
            CreatedAt = entity.CreatedAt
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await _context.ContentFilters
            .AsNoTracking()
            .Where(f => f.Id == entity.Id)
            .SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Is.Not.Null);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Name)).EqualTo("UpdatedName"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedAt)).Not.Null);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new ContentFilter
        {
            Id = Guid.NewGuid(),
            Name = "NonExistent",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_DoesNotCreateAuditRecord_WhenNoRowsUpdated()
    {
        // Arrange
        var model = new ContentFilter
        {
            Id = Guid.NewGuid(),
            Name = "NonExistent",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _subject.UpdateAsync(model, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        Assert.That(auditRecords, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_DeletesContentFilter()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "ToDelete",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.ContentFilters.AnyAsync(f => f.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entityExists, Is.False);
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_DoesNotCreateAuditRecord_WhenContentFilterDoesNotExist()
    {
        // Act
        await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        Assert.That(auditRecords, Is.Empty);
    }

    [Test]
    public async Task PatchAsync_PatchesName()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "OriginalName",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync();

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Name = new() { Value = "PatchedName" }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ContentFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Is.Not.Null);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Name)).EqualTo("PatchedName"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedAt)).Not.Null);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesCriteria()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "TestFilter",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync();

        var newCriteria = new ContentFilterCriteria();
        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Criteria = new() { Value = newCriteria }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ContentFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Is.Not.Null);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Criteria)).Not.Null);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesOwnerLock()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "TestFilter",
            Criteria = CreateTestCriteria(),
            OwnerLock = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync();

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            OwnerLock = new() { Value = true }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ContentFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Is.Not.Null);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.OwnerLock)).True);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_AddOnly()
    {
        // Arrange
        var existingTag = new Tag { Name = "existing" };
        var newTag1 = new Tag { Name = "newTag1" };
        var newTag2 = new Tag { Name = "newTag2" };
        _context.Tags.AddRange(existingTag, newTag1, newTag2);

        var entity = new ContentFilter
        {
            Name = "TestFilter",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        existingTag.ContentFilters.Add(entity);
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync();

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Tags = new() { Add = ["newTag1", "newTag2"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tags = await _context.Tags
            .AsNoTracking()
            .Where(t => t.ContentFilters.Any(f => f.Id == entity.Id))
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(tags, Has.Count.EqualTo(3));
            Assert.That(tags, Has.One.Property(nameof(Tag.Name)).EqualTo("existing"));
            Assert.That(tags, Has.One.Property(nameof(Tag.Name)).EqualTo("newTag1"));
            Assert.That(tags, Has.One.Property(nameof(Tag.Name)).EqualTo("newTag2"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_RemoveOnly()
    {
        // Arrange
        var keepTag = new Tag { Name = "keepMe" };
        var removeTag = new Tag { Name = "removeMe" };
        _context.Tags.AddRange(keepTag, removeTag);

        var entity = new ContentFilter
        {
            Name = "TestFilter",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        keepTag.ContentFilters.Add(entity);
        removeTag.ContentFilters.Add(entity);
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync();

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Tags = new() { Remove = ["removeMe"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tags = await _context.Tags
            .AsNoTracking()
            .Where(t => t.ContentFilters.Any(f => f.Id == entity.Id))
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(tags, Has.Count.EqualTo(1));
            Assert.That(tags, Has.One.Property(nameof(Tag.Name)).EqualTo("keepMe"));
            Assert.That(tags, Has.None.Property(nameof(Tag.Name)).EqualTo("removeMe"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_AddAndRemove()
    {
        // Arrange
        var existingTag = new Tag { Name = "existing" };
        var toRemoveTag = new Tag { Name = "toRemove" };
        var toAddTag = new Tag { Name = "toAdd" };
        _context.Tags.AddRange(existingTag, toRemoveTag, toAddTag);

        var entity = new ContentFilter
        {
            Name = "TestFilter",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        existingTag.ContentFilters.Add(entity);
        toRemoveTag.ContentFilters.Add(entity);
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync();

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Tags = new() { Add = ["toAdd"], Remove = ["toRemove"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tags = await _context.Tags
            .AsNoTracking()
            .Where(t => t.ContentFilters.Any(f => f.Id == entity.Id))
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(tags, Has.Count.EqualTo(2));
            Assert.That(tags, Has.One.Property(nameof(Tag.Name)).EqualTo("existing"));
            Assert.That(tags, Has.One.Property(nameof(Tag.Name)).EqualTo("toAdd"));
            Assert.That(tags, Has.None.Property(nameof(Tag.Name)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesNewTag_WhenTagDoesNotExist()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "TestFilter",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync();

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Tags = new() { Add = ["brandNewTag"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tags = await _context.Tags
            .AsNoTracking()
            .Where(t => t.ContentFilters.Any(f => f.Id == entity.Id))
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(tags, Has.Count.EqualTo(1));
            Assert.That(tags, Has.One.Property(nameof(Tag.Name)).EqualTo("brandNewTag"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new ContentFilterPatch
        {
            Id = Guid.NewGuid(),
            Name = new() { Value = "NewName" }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.False);
    }
}
