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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        _context = new(new DbContextOptionsBuilder<MagpieDbContext>().UseSqlite("Data Source=:memory:").Options);

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

        var loggerFactory = NullLoggerFactory.Instance;
        var auditRepo = new AuditRecordRepository(
            _context,
            Moq.Mock.Of<Sdk.Services.IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance));
        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        _subject = new SubjectFilterRepository(
            _context,
            auditRepo,
            transactionManager,
            Moq.Mock.Of<Sdk.Services.IEventBus>(),
            loggerFactory.CreateLogger<SubjectFilterRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsSubjectFilter_WhenFilterExists()
    {
        // Arrange
        var filter = new SubjectFilter
        {
            Name = "TestFilter",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.Add(filter);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(filter.Id).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestFilter"));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllSubjectFilters_WhenFiltersExist()
    {
        // Arrange
        var filter1 = new SubjectFilter
        {
            Name = "Filter1",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };
        var filter2 = new SubjectFilter
        {
            Name = "Filter2",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.AddRange(filter1, filter2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoSubjectFilters()
    {
        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetByNameAsync_ReturnsSubjectFilter_WhenNamedFilterExists()
    {
        // Arrange
        var filter = new SubjectFilter
        {
            Name = "UniqueFilterName",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.Add(filter);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(new() { Name = "UniqueFilterName" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false))
            .Items
            .SingleOrDefault();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("UniqueFilterName"));
    }

    [Test]
    public async Task GetByNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = (await _subject.QueryAsync(new() { Name = "NonExistentFilter" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false))
            .Items
            .SingleOrDefault();

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListSharedByTagAsync_ReturnsSubjectFilters_WhenTagMatches()
    {
        // Arrange
        foreach (var tagName in new[] { "shared", "unique" })
        {
            if (!_context.Tags.Any(t => t.Name == tagName))
            {
                _context.Tags.Add(new Tag { Name = tagName });
            }
        }

        await _context.SaveChangesAsync().ConfigureAwait(false);
        var tag1 = _context.Tags.First(t => t.Name == "shared");
        var tag2 = _context.Tags.First(t => t.Name == "unique");

        var filter1 = new SubjectFilter
        {
            Name = "Filter1",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };
        var filter2 = new SubjectFilter
        {
            Name = "Filter2",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };
        var filter3 = new SubjectFilter
        {
            Name = "Filter3",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };

        tag1.SubjectFilters.Add(filter1);
        tag1.SubjectFilters.Add(filter2);
        tag2.SubjectFilters.Add(filter3);

        _context.SubjectFilters.AddRange(filter1, filter2, filter3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(
            new() { Tag = "shared", SharedOnly = true },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items.ToList();

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListSharedByTagAsync_ExcludesOwnedFilters_WhenTagMatchesPrivateFilter()
    {
        // Arrange
        foreach (var tagName in new[] { "shared" })
        {
            if (!_context.Tags.Any(t => t.Name == tagName))
            {
                _context.Tags.Add(new Tag { Name = tagName });
            }
        }

        await _context.SaveChangesAsync().ConfigureAwait(false);
        var tag = _context.Tags.First(t => t.Name == "shared");

        var sharedFilter = new SubjectFilter
        {
            Name = "SharedFilter",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };
        var privateFilter = new SubjectFilter
        {
            Name = "PrivateFilter",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            OwnerUserId = _userId,
            CreatedAt = DateTime.UtcNow
        };

        tag.SubjectFilters.Add(sharedFilter);
        tag.SubjectFilters.Add(privateFilter);
        _context.SubjectFilters.AddRange(sharedFilter, privateFilter);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(
            new() { Tag = "shared", SharedOnly = true },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items.ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result, Has.One.Property(nameof(SubjectFilter.Name)).EqualTo("SharedFilter"));
        }
    }

    [Test]
    public async Task ListSharedByTagAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = (await _subject.QueryAsync(
            new() { Tag = "nonexistent", SharedOnly = true },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items.ToList();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListSharedAsync_ExcludesOwnedFilters_WhenPrivateFiltersExist()
    {
        // Arrange
        var sharedFilter = new SubjectFilter
        {
            Name = "SharedFilter",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };
        var privateFilter = new SubjectFilter
        {
            Name = "PrivateFilter",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            OwnerUserId = _userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.AddRange(sharedFilter, privateFilter);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(new() { SharedOnly = true },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items
            .ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result, Has.One.Property(nameof(SubjectFilter.Name)).EqualTo("SharedFilter"));
        }
    }

    [Test]
    public async Task ListSharedAsync_ReturnsEmptyList_WhenNoSharedFilters()
    {
        // Arrange
        var privateFilter = new SubjectFilter
        {
            Name = "PrivateFilter",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            OwnerUserId = _userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.Add(privateFilter);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(new() { SharedOnly = true },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items
            .ToList();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListByUserIdAsync_ReturnsSubjectFilters_WhenUserOwnsFilters()
    {
        // Arrange
        var filter1 = new SubjectFilter
        {
            Name = "Filter1",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow,
            OwnerUserId = _userId
        };
        var filter2 = new SubjectFilter
        {
            Name = "Filter2",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow,
            OwnerUserId = _userId
        };
        var filter3 = new SubjectFilter
        {
            Name = "Filter3",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow,
            OwnerUserId = Guid.NewGuid()
        };
        _context.SubjectFilters.AddRange(filter1, filter2, filter3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(new() { OwnerUserId = _userId },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false))
            .Items
            .ToList();

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task CreateAsync_CreatesSubjectFilter_WhenRequestIsValid()
    {
        // Arrange
        var model = new SubjectFilter
        {
            Name = "NewFilter",
            Criteria = new SubjectFilterCriteria { Kind = new() }
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var filter = await _context.SubjectFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Name == "NewFilter").ConfigureAwait(false);
        var audit = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewFilter"));
            Assert.That(filter, Has.Property(nameof(filter.CreatedByUserId)).EqualTo(_userId));
            Assert.That(
                audit,
                Has.One
                    .With.Property(nameof(AuditRecord.Operation)).EqualTo("Create")
                    .And.Property(nameof(AuditRecord.EntityType)).EqualTo(nameof(SubjectFilter))
                    .And.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesSubjectFilter_WhenFilterExists()
    {
        // Arrange
        var filter = new SubjectFilter
        {
            Name = "OriginalName",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.Add(filter);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        filter.Name = "UpdatedName";

        // Act
        var result = await _subject.UpdateAsync(filter, _userId).ConfigureAwait(false);

        // Assert
        var updatedFilter = await _context.SubjectFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == filter.Id).ConfigureAwait(false);
        var audit = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedFilter, Has.Property(nameof(updatedFilter.Name)).EqualTo("UpdatedName"));
            Assert.That(updatedFilter, Has.Property(nameof(updatedFilter.ModifiedAt)).Not.Null);
            Assert.That(updatedFilter, Has.Property(nameof(updatedFilter.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(
                audit,
                Has.One
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
            Criteria = new SubjectFilterCriteria { Kind = new() }
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_DeletesSubjectFilter_WhenFilterExists()
    {
        // Arrange
        var filter = new SubjectFilter
        {
            Name = "ToDelete",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.Add(filter);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(filter.Id, _userId).ConfigureAwait(false);

        // Assert
        var exists = await _context.SubjectFilters.AnyAsync(f => f.Id == filter.Id).ConfigureAwait(false);
        var audit = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(exists, Is.False);
            Assert.That(
                audit,
                Has.One
                    .With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete")
                    .And.Property(nameof(AuditRecord.EntityType)).EqualTo(nameof(SubjectFilter))
                    .And.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_UpdatesName_WhenNameIsProvided()
    {
        // Arrange
        var filter = new SubjectFilter
        {
            Name = "OriginalName",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.Add(filter);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new SubjectFilterPatch
        {
            Id = filter.Id,
            Name = new() { Value = "PatchedName" }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var updatedFilter = await _context.SubjectFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == filter.Id).ConfigureAwait(false);
        var audit = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedFilter, Has.Property(nameof(updatedFilter.Name)).EqualTo("PatchedName"));
            Assert.That(updatedFilter, Has.Property(nameof(updatedFilter.ModifiedAt)).Not.Null);
            Assert.That(updatedFilter, Has.Property(nameof(updatedFilter.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(
                audit,
                Has.One
                    .With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch")
                    .And.Property(nameof(AuditRecord.EntityType)).EqualTo(nameof(SubjectFilter))
                    .And.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesCriteria_WhenCriteriaAreProvided()
    {
        // Arrange
        var filter = new SubjectFilter
        {
            Name = "TestFilter",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };
        _context.SubjectFilters.Add(filter);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var newCriteria = new SubjectFilterCriteria
        {
            Kind = new()
            {
                And = [new() { Values = ["type-a"] }]
            }
        };
        var patch = new SubjectFilterPatch
        {
            Id = filter.Id,
            Criteria = new() { Value = newCriteria }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var updatedFilter = await _context.SubjectFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == filter.Id).ConfigureAwait(false);
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
        foreach (var tagName in new[] { "existing", "newTag1", "newTag2" })
        {
            if (!_context.Tags.Any(t => t.Name == tagName))
            {
                _context.Tags.Add(new Tag { Name = tagName });
            }
        }

        await _context.SaveChangesAsync().ConfigureAwait(false);
        var existingTag = _context.Tags.First(t => t.Name == "existing");

        var filter = new SubjectFilter
        {
            Name = "TestFilter",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };
        existingTag.SubjectFilters.Add(filter);
        _context.SubjectFilters.Add(filter);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new SubjectFilterPatch
        {
            Id = filter.Id,
            Tags = new() { Add = ["newTag1", "newTag2"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var tags = await _context.Tags
            .Where(t => t.SubjectFilters.Any(f => f.Id == filter.Id))
            .Select(t => t.Name)
            .ToListAsync().ConfigureAwait(false);
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
        foreach (var tagName in new[] { "keepMe", "removeMe" })
        {
            if (!_context.Tags.Any(t => t.Name == tagName))
            {
                _context.Tags.Add(new Tag { Name = tagName });
            }
        }

        await _context.SaveChangesAsync().ConfigureAwait(false);
        var keepTag = _context.Tags.First(t => t.Name == "keepMe");
        var removeTag = _context.Tags.First(t => t.Name == "removeMe");

        var filter = new SubjectFilter
        {
            Name = "TestFilter",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };
        keepTag.SubjectFilters.Add(filter);
        removeTag.SubjectFilters.Add(filter);
        _context.SubjectFilters.Add(filter);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new SubjectFilterPatch
        {
            Id = filter.Id,
            Tags = new() { Remove = ["removeMe"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var tags = await _context.Tags
            .Where(t => t.SubjectFilters.Any(f => f.Id == filter.Id))
            .Select(t => t.Name)
            .ToListAsync().ConfigureAwait(false);
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
        foreach (var tagName in new[] { "existing", "toRemove", "toAdd" })
        {
            if (!_context.Tags.Any(t => t.Name == tagName))
            {
                _context.Tags.Add(new Tag { Name = tagName });
            }
        }

        await _context.SaveChangesAsync().ConfigureAwait(false);
        var existingTag = _context.Tags.First(t => t.Name == "existing");
        var removeTag = _context.Tags.First(t => t.Name == "toRemove");

        var filter = new SubjectFilter
        {
            Name = "TestFilter",
            Criteria = new SubjectFilterCriteria { Kind = new() },
            CreatedAt = DateTime.UtcNow
        };
        existingTag.SubjectFilters.Add(filter);
        removeTag.SubjectFilters.Add(filter);
        _context.SubjectFilters.Add(filter);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new SubjectFilterPatch
        {
            Id = filter.Id,
            Tags = new() { Add = ["toAdd"], Remove = ["toRemove"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var tags = await _context.Tags
            .Where(t => t.SubjectFilters.Any(f => f.Id == filter.Id))
            .Select(t => t.Name)
            .ToListAsync().ConfigureAwait(false);
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
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }
}
