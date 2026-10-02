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

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class SubjectFilterRepositoryTests
{
    private Mock<IEventBus> _updateEvents = null!;

    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private ISubjectFilterRepository _subject = null!;
    private Guid _userId;

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
        _context = new(
            new DbContextOptionsBuilder<MagpieDbContext>().UseSqlite(connection, contextOwnsConnection: true).Options);

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

        var auditRepo = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        _updateEvents = new Mock<IEventBus>();
        _subject = new SubjectFilterRepository(
            _context,
            auditRepo,
            transactionManager,
            _updateEvents.Object,
            NullLogger<SubjectFilterRepository>.Instance,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
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
        _context.Tags.Add(new Tag { Name = "shared" });
        _context.Tags.Add(new Tag { Name = "unique" });

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
        _context.Tags.Add(new Tag { Name = "shared" });

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
    public async Task UpdateAsync_PublishesUpdatedEventWithChangedFields_WhenFilterExists()
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
        await _subject.UpdateAsync(filter, _userId).ConfigureAwait(false);

        // Assert
        _updateEvents.Verify(bus => bus.PublishAsync(
            It.Is<MagpieEvent>(e =>
                e.Name == "subjectFilter.updated" &&
                e.Context.ContainsKey("subjectFilter.changedFields") &&
                e.Context["subjectFilter.changedFields"] == "name"),
            It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task PatchAsync_PatchesTags_WhenTagsAreAdded()
    {
        // Arrange
        _context.Tags.Add(new Tag { Name = "existing" });
        _context.Tags.Add(new Tag { Name = "newTag1" });
        _context.Tags.Add(new Tag { Name = "newTag2" });

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
    public async Task PatchAsync_PatchesTags_WhenTagsAreRemoved()
    {
        // Arrange
        _context.Tags.Add(new Tag { Name = "keepMe" });
        _context.Tags.Add(new Tag { Name = "removeMe" });

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
        _context.ChangeTracker.Clear();

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
    public async Task PatchAsync_PatchesTags_WhenTagsAreAddedAndRemoved()
    {
        // Arrange
        _context.Tags.Add(new Tag { Name = "existing" });
        _context.Tags.Add(new Tag { Name = "toRemove" });
        _context.Tags.Add(new Tag { Name = "toAdd" });

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
        _context.ChangeTracker.Clear();

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
