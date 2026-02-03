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
public class SubjectFilterRepositoryTests
{
    private MagpieDbContext _context = null!;
    private ISubjectFilterRepository _subject = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _context = new(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var user = new User
        {
            Username = "TestUser",
            ProviderType = "Local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.SaveChanges();

        _userId = user.Id;

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var auditRepo = new AuditRecordRepository(_context, loggerFactory.CreateLogger<AuditRecordRepository>());
        var transactionManager = new TransactionManager(_context);

        _subject = new SubjectFilterRepository(
            _context,
            auditRepo,
            transactionManager,
            loggerFactory.CreateLogger<SubjectFilterRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    private void EnsureTagsExist(params string[] tagNames)
    {
        foreach (var tagName in tagNames)
        {
            if (!_context.Tags.Any(t => t.Name == tagName))
            {
                _context.Tags.Add(new Tag { Name = tagName });
            }
        }
        _context.SaveChanges();
    }

    private static SubjectFilterCriteria CreateTestCriteria()
    {
        return new SubjectFilterCriteria
        {
            Kind = new()
        };
    }

    [Test]
    public async Task GetByIdAsync_ReturnsSubjectFilter()
    {
        // Arrange
        var filter = new SubjectFilter
        {
            Name = "TestFilter",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.Add(filter);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(filter.Id);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestFilter"));
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
    public async Task ListAllAsync_ReturnsAllSubjectFilters()
    {
        // Arrange
        var filter1 = new SubjectFilter
        {
            Name = "Filter1",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        var filter2 = new SubjectFilter
        {
            Name = "Filter2",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.AddRange(filter1, filter2);
        _context.SaveChanges();

        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoSubjectFilters()
    {
        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetByNameAsync_ReturnsSubjectFilter()
    {
        // Arrange
        var filter = new SubjectFilter
        {
            Name = "UniqueFilterName",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.Add(filter);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("UniqueFilterName");

        // Assert
        Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("UniqueFilterName"));
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
    public async Task ListByTagAsync_ReturnsSubjectFilters()
    {
        // Arrange
        EnsureTagsExist("shared", "unique");
        var tag1 = _context.Tags.First(t => t.Name == "shared");
        var tag2 = _context.Tags.First(t => t.Name == "unique");

        var filter1 = new SubjectFilter
        {
            Name = "Filter1",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        var filter2 = new SubjectFilter
        {
            Name = "Filter2",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        var filter3 = new SubjectFilter
        {
            Name = "Filter3",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };

        tag1.SubjectFilters.Add(filter1);
        tag1.SubjectFilters.Add(filter2);
        tag2.SubjectFilters.Add(filter3);

        _context.SubjectFilters.AddRange(filter1, filter2, filter3);
        _context.SaveChanges();

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
    public async Task ListByUserIdAsync_ReturnsSubjectFilters()
    {
        // Arrange
        var filter1 = new SubjectFilter
        {
            Name = "Filter1",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var filter2 = new SubjectFilter
        {
            Name = "Filter2",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var filter3 = new SubjectFilter
        {
            Name = "Filter3",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _context.SubjectFilters.AddRange(filter1, filter2, filter3);
        _context.SaveChanges();

        // Act
        var result = await _subject.ListByUserIdAsync(_userId);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task CreateAsync_CreatesSubjectFilter()
    {
        // Arrange
        var model = new SubjectFilter
        {
            Name = "NewFilter",
            Criteria = CreateTestCriteria()
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var filter = await _context.SubjectFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Name == "NewFilter");
        var audit = await _context.AuditRecords.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewFilter"));
            Assert.That(filter, Has.Property(nameof(filter.CreatedByUserId)).EqualTo(_userId));
            Assert.That(audit, Has.One
                .With.Property(nameof(AuditRecord.Operation)).EqualTo("Create")
                .And.Property(nameof(AuditRecord.EntityType)).EqualTo(nameof(SubjectFilter))
                .And.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesSubjectFilter()
    {
        // Arrange
        var filter = new SubjectFilter
        {
            Name = "OriginalName",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.Add(filter);
        _context.SaveChanges();

        filter.Name = "UpdatedName";

        // Act
        var result = await _subject.UpdateAsync(filter, _userId);

        // Assert
        var updatedFilter = await _context.SubjectFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == filter.Id);
        var audit = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedFilter, Has.Property(nameof(updatedFilter.Name)).EqualTo("UpdatedName"));
            Assert.That(updatedFilter, Has.Property(nameof(updatedFilter.ModifiedAt)).Not.Null);
            Assert.That(updatedFilter, Has.Property(nameof(updatedFilter.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(audit, Has.One
                .With.Property(nameof(AuditRecord.Operation)).EqualTo("Update")
                .And.Property(nameof(AuditRecord.EntityType)).EqualTo(nameof(SubjectFilter))
                .And.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new SubjectFilter
        {
            Id = Guid.NewGuid(),
            Name = "NonExistent",
            Criteria = CreateTestCriteria()
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_DeletesSubjectFilter()
    {
        // Arrange
        var filter = new SubjectFilter
        {
            Name = "ToDelete",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.Add(filter);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(filter.Id, _userId);

        // Assert
        var exists = await _context.SubjectFilters.AnyAsync(f => f.Id == filter.Id);
        var audit = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(exists, Is.False);
            Assert.That(audit, Has.One
                .With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete")
                .And.Property(nameof(AuditRecord.EntityType)).EqualTo(nameof(SubjectFilter))
                .And.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
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
    public async Task PatchAsync_PatchesName()
    {
        // Arrange
        var filter = new SubjectFilter
        {
            Name = "OriginalName",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.Add(filter);
        _context.SaveChanges();

        var patch = new SubjectFilterPatch
        {
            Id = filter.Id,
            Name = new() { Value = "PatchedName" }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedFilter = await _context.SubjectFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == filter.Id);
        var audit = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedFilter, Has.Property(nameof(updatedFilter.Name)).EqualTo("PatchedName"));
            Assert.That(updatedFilter, Has.Property(nameof(updatedFilter.ModifiedAt)).Not.Null);
            Assert.That(updatedFilter, Has.Property(nameof(updatedFilter.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(audit, Has.One
                .With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch")
                .And.Property(nameof(AuditRecord.EntityType)).EqualTo(nameof(SubjectFilter))
                .And.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesCriteria()
    {
        // Arrange
        var filter = new SubjectFilter
        {
            Name = "TestFilter",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.Add(filter);
        _context.SaveChanges();

        var newCriteria = new SubjectFilterCriteria
        {
            Kind = new()
            {
                And = [new() { Values = ["Movie"] }]
            }
        };
        var patch = new SubjectFilterPatch
        {
            Id = filter.Id,
            Criteria = new() { Value = newCriteria }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedFilter = await _context.SubjectFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == filter.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedFilter, Is.Not.Null);
            Assert.That(updatedFilter!.Criteria.Kind!.And.ToList(), Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_AddOnly()
    {
        // Arrange
        EnsureTagsExist("existing", "newTag1", "newTag2");
        var existingTag = _context.Tags.First(t => t.Name == "existing");

        var filter = new SubjectFilter
        {
            Name = "TestFilter",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        existingTag.SubjectFilters.Add(filter);
        _context.SubjectFilters.Add(filter);
        _context.SaveChanges();

        var patch = new SubjectFilterPatch
        {
            Id = filter.Id,
            Tags = new() { Add = ["newTag1", "newTag2"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tags = await _context.Tags
            .Where(t => t.SubjectFilters.Any(f => f.Id == filter.Id))
            .Select(t => t.Name)
            .ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(tags, Has.Count.EqualTo(3));
            Assert.That(tags, Does.Contain("existing"));
            Assert.That(tags, Does.Contain("newTag1"));
            Assert.That(tags, Does.Contain("newTag2"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_RemoveOnly()
    {
        // Arrange
        EnsureTagsExist("keepMe", "removeMe");
        var keepTag = _context.Tags.First(t => t.Name == "keepMe");
        var removeTag = _context.Tags.First(t => t.Name == "removeMe");

        var filter = new SubjectFilter
        {
            Name = "TestFilter",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        keepTag.SubjectFilters.Add(filter);
        removeTag.SubjectFilters.Add(filter);
        _context.SubjectFilters.Add(filter);
        _context.SaveChanges();

        var patch = new SubjectFilterPatch
        {
            Id = filter.Id,
            Tags = new() { Remove = ["removeMe"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tags = await _context.Tags
            .Where(t => t.SubjectFilters.Any(f => f.Id == filter.Id))
            .Select(t => t.Name)
            .ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(tags, Has.Count.EqualTo(1));
            Assert.That(tags, Does.Contain("keepMe"));
            Assert.That(tags, Does.Not.Contain("removeMe"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_AddAndRemove()
    {
        // Arrange
        EnsureTagsExist("existing", "toRemove", "toAdd");
        var existingTag = _context.Tags.First(t => t.Name == "existing");
        var removeTag = _context.Tags.First(t => t.Name == "toRemove");

        var filter = new SubjectFilter
        {
            Name = "TestFilter",
            Criteria = CreateTestCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        existingTag.SubjectFilters.Add(filter);
        removeTag.SubjectFilters.Add(filter);
        _context.SubjectFilters.Add(filter);
        _context.SaveChanges();

        var patch = new SubjectFilterPatch
        {
            Id = filter.Id,
            Tags = new() { Add = ["toAdd"], Remove = ["toRemove"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tags = await _context.Tags
            .Where(t => t.SubjectFilters.Any(f => f.Id == filter.Id))
            .Select(t => t.Name)
            .ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(tags, Has.Count.EqualTo(2));
            Assert.That(tags, Does.Contain("existing"));
            Assert.That(tags, Does.Contain("toAdd"));
            Assert.That(tags, Does.Not.Contain("toRemove"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new SubjectFilterPatch
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
