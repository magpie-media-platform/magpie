using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ContentFilterRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IContentFilterRepository _subject = null!;
    private Mock<IEventBus> _eventBus = null!;
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

        var loggerFactory = NullLoggerFactory.Instance;
        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        _eventBus = new Mock<IEventBus>();
        _eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _subject = new ContentFilterRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            _eventBus.Object,
            loggerFactory.CreateLogger<ContentFilterRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsContentFilter_WhenFilterExists()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "TestFilter",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(entity.Id).ConfigureAwait(false);

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
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllContentFilters_WhenFiltersExist()
    {
        // Arrange
        var entity1 = new ContentFilter
        {
            Name = "Filter1",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        var entity2 = new ContentFilter
        {
            Name = "Filter2",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.AddRange(entity1, entity2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoContentFilters()
    {
        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetByNameAsync_ReturnsContentFilter_WhenNamedFilterExists()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "UniqueFilterName",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(new() { Name = "UniqueFilterName" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false))
            .Items
            .SingleOrDefault();

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
        var result = (await _subject.QueryAsync(new() { Name = "NonExistentFilter" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false))
            .Items
            .SingleOrDefault();

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListSharedByTagAsync_ReturnsContentFilters_WhenFiltersShareTag()
    {
        // Arrange
        var sharedTag = new Tag { Name = "shared" };
        var uniqueTag = new Tag { Name = "unique" };
        _context.Tags.AddRange(sharedTag, uniqueTag);

        var entity1 = new ContentFilter
        {
            Name = "Filter1",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        var entity2 = new ContentFilter
        {
            Name = "Filter2",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        var entity3 = new ContentFilter
        {
            Name = "Filter3",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        sharedTag.ContentFilters.Add(entity1);
        sharedTag.ContentFilters.Add(entity2);
        uniqueTag.ContentFilters.Add(entity3);
        _context.ContentFilters.AddRange(entity1, entity2, entity3);
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
        var tag = new Tag { Name = "shared" };
        _context.Tags.Add(tag);

        var sharedFilter = new ContentFilter
        {
            Name = "SharedFilter",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        var privateFilter = new ContentFilter
        {
            Name = "PrivateFilter",
            Criteria = new ContentFilterCriteria(),
            OwnerUserId = _userId,
            CreatedAt = DateTime.UtcNow
        };
        tag.ContentFilters.Add(sharedFilter);
        tag.ContentFilters.Add(privateFilter);
        _context.ContentFilters.AddRange(sharedFilter, privateFilter);
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
            Assert.That(result, Has.One.Property(nameof(ContentFilter.Name)).EqualTo("SharedFilter"));
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
        var sharedFilter = new ContentFilter
        {
            Name = "SharedFilter",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        var privateFilter = new ContentFilter
        {
            Name = "PrivateFilter",
            Criteria = new ContentFilterCriteria(),
            OwnerUserId = _userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.AddRange(sharedFilter, privateFilter);
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
            Assert.That(result, Has.One.Property(nameof(ContentFilter.Name)).EqualTo("SharedFilter"));
        }
    }

    [Test]
    public async Task ListSharedAsync_ReturnsEmptyList_WhenNoSharedFilters()
    {
        // Arrange
        var privateFilter = new ContentFilter
        {
            Name = "PrivateFilter",
            Criteria = new ContentFilterCriteria(),
            OwnerUserId = _userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(privateFilter);
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
    public async Task ListByUserIdAsync_ReturnsContentFiltersForUser_WhenUserOwnsFilters()
    {
        // Arrange
        var entity1 = new ContentFilter
        {
            Name = "UserFilter1",
            Criteria = new ContentFilterCriteria(),
            OwnerUserId = _userId,
            CreatedAt = DateTime.UtcNow
        };
        var entity2 = new ContentFilter
        {
            Name = "UserFilter2",
            Criteria = new ContentFilterCriteria(),
            OwnerUserId = _userId,
            CreatedAt = DateTime.UtcNow
        };
        var entity3 = new ContentFilter
        {
            Name = "OtherFilter",
            Criteria = new ContentFilterCriteria(),
            OwnerUserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.AddRange(entity1, entity2, entity3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(new() { OwnerUserId = _userId },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false))
            .Items
            .ToList();

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
            Criteria = new ContentFilterCriteria(),
            OwnerUserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(new() { OwnerUserId = _userId },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false))
            .Items
            .ToList();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesContentFilter_WhenRequestIsValid()
    {
        // Arrange
        var model = new ContentFilter
        {
            Name = "NewFilter",
            Criteria = new ContentFilterCriteria()
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.ContentFilters
            .AsNoTracking()
            .Where(f => f.Name == "NewFilter")
            .SingleOrDefaultAsync().ConfigureAwait(false);
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);

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
    public async Task CreateAsync_CreatesContentFilter_WhenNullUserIdIsProvided()
    {
        // Arrange
        var model = new ContentFilter
        {
            Name = "SystemFilter",
            Criteria = new ContentFilterCriteria()
        };

        // Act
        var result = await _subject.CreateAsync(model, null).ConfigureAwait(false);

        // Assert
        var entity = await _context.ContentFilters
            .AsNoTracking()
            .Where(f => f.Name == "SystemFilter")
            .SingleOrDefaultAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("SystemFilter"));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity, Has.Property(nameof(entity.CreatedByUserId)).EqualTo(null));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesContentFilter_WhenFilterExists()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "OriginalName",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var model = new ContentFilter
        {
            Id = entity.Id,
            Name = "UpdatedName",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = entity.CreatedAt
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await _context.ContentFilters
            .AsNoTracking()
            .Where(f => f.Id == entity.Id)
            .SingleOrDefaultAsync().ConfigureAwait(false);

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
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

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
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        Assert.That(auditRecords, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_DeletesContentFilter_WhenFilterExists()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "ToDelete",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId).ConfigureAwait(false);

        // Assert
        var entityExists = await _context.ContentFilters.AnyAsync(f => f.Id == entity.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entityExists, Is.False);
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalseAndWritesNoAuditRecord_WhenNotFound()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(auditRecords, Is.Empty);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesName_WhenNameIsSet()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "OriginalName",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Name = new() { Value = "PatchedName" }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await _context.ContentFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == entity.Id).ConfigureAwait(false);

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
    public async Task PatchAsync_PatchesCriteria_WhenCriteriaIsSet()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "TestFilter",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var newCriteria = new ContentFilterCriteria();
        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Criteria = new() { Value = newCriteria }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await _context.ContentFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == entity.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Is.Not.Null);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Criteria)).Not.Null);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesOwnerLock_WhenOwnerLockIsSet()
    {
        // Arrange
        var entity = new ContentFilter
        {
            Name = "TestFilter",
            Criteria = new ContentFilterCriteria(),
            OwnerLock = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            OwnerLock = new() { Value = true }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await _context.ContentFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == entity.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Is.Not.Null);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.OwnerLock)).True);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_WhenTagsAreAdded()
    {
        // Arrange
        var existingTag = new Tag { Name = "existing" };
        var newTag1 = new Tag { Name = "newTag1" };
        var newTag2 = new Tag { Name = "newTag2" };
        _context.Tags.AddRange(existingTag, newTag1, newTag2);

        var entity = new ContentFilter
        {
            Name = "TestFilter",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        existingTag.ContentFilters.Add(entity);
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Tags = new() { Add = ["newTag1", "newTag2"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var tags = await _context.Tags
            .AsNoTracking()
            .Where(t => t.ContentFilters.Any(f => f.Id == entity.Id))
            .ToListAsync().ConfigureAwait(false);

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
    public async Task PatchAsync_PatchesTags_WhenTagsAreRemoved()
    {
        // Arrange
        var keepTag = new Tag { Name = "keepMe" };
        var removeTag = new Tag { Name = "removeMe" };
        _context.Tags.AddRange(keepTag, removeTag);

        var entity = new ContentFilter
        {
            Name = "TestFilter",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        keepTag.ContentFilters.Add(entity);
        removeTag.ContentFilters.Add(entity);
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Tags = new() { Remove = ["removeMe"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var tags = await _context.Tags
            .AsNoTracking()
            .Where(t => t.ContentFilters.Any(f => f.Id == entity.Id))
            .ToListAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(tags, Has.Count.EqualTo(1));
            Assert.That(tags, Has.One.Property(nameof(Tag.Name)).EqualTo("keepMe"));
            Assert.That(tags, Has.None.Property(nameof(Tag.Name)).EqualTo("removeMe"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_WhenTagsAreAddedAndRemoved()
    {
        // Arrange
        var existingTag = new Tag { Name = "existing" };
        var toRemoveTag = new Tag { Name = "toRemove" };
        var toAddTag = new Tag { Name = "toAdd" };
        _context.Tags.AddRange(existingTag, toRemoveTag, toAddTag);

        var entity = new ContentFilter
        {
            Name = "TestFilter",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        existingTag.ContentFilters.Add(entity);
        toRemoveTag.ContentFilters.Add(entity);
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Tags = new() { Add = ["toAdd"], Remove = ["toRemove"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var tags = await _context.Tags
            .AsNoTracking()
            .Where(t => t.ContentFilters.Any(f => f.Id == entity.Id))
            .ToListAsync().ConfigureAwait(false);

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
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Tags = new() { Add = ["brandNewTag"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var tags = await _context.Tags
            .AsNoTracking()
            .Where(t => t.ContentFilters.Any(f => f.Id == entity.Id))
            .ToListAsync().ConfigureAwait(false);

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
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CreateUpdateDeleteAsync_PublishesLifecycleEvents_WhenLifecycleCompletes()
    {
        // Arrange
        var created = await _subject.CreateAsync(
            new ContentFilter
            {
                Name = "Lifecycle",
                Criteria = new ContentFilterCriteria()
            },
            _userId).ConfigureAwait(false);

        // Act
        await _subject.PatchAsync(
            new ContentFilterPatch
            {
                Id = created.Id,
                Name = new() { Value = "Lifecycle updated" }
            },
            _userId).ConfigureAwait(false);
        await _subject.DeleteAsync(created.Id, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _eventBus.Verify(
                bus => bus.PublishAsync(
                    It.Is<MagpieEvent>(magpieEvent => magpieEvent.Name == "contentFilter.created"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _eventBus.Verify(
                bus => bus.PublishAsync(
                    It.Is<MagpieEvent>(magpieEvent => magpieEvent.Name == "contentFilter.updated"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _eventBus.Verify(
                bus => bus.PublishAsync(
                    It.Is<MagpieEvent>(magpieEvent => magpieEvent.Name == "contentFilter.deleted"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
