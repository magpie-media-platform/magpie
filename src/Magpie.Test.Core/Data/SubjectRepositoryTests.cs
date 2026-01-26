using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models.Content;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using SubjectEntity = Magpie.Data.Database.Entities.Subject;
using SubjectTagEntity = Magpie.Data.Database.Entities.SubjectTag;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class SubjectRepositoryTests
{
    private MagpieDbContext _context = null!;
    private ISubjectRepository _subject = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _context = new(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var user = new UserEntity
        {
            Username = "TestUser",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(user);
        _context.SaveChanges();

        _userId = user.Id;

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var historyRepo = new HistoryRepository(_context, loggerFactory.CreateLogger<HistoryRepository>());

        _subject = new SubjectRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<SubjectRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    private void EnsureTagsExist(params string[] tagNames)
    {
        foreach (var tagName in tagNames)
        {
            if (!_context.Tags.Any(t => t.Name == tagName))
            {
                _context.Tags.Add(new() { Name = tagName });
            }
        }
        _context.SaveChanges();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsSubject()
    {
        // Arrange
        var entity = new SubjectEntity
        {
            Kind = "Person",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Subjects.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Subject?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Subject.Kind)).EqualTo("Person"));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Subject?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_IncludesTags()
    {
        // Arrange
        EnsureTagsExist("actor", "director");
        var entity = new SubjectEntity
        {
            Kind = "Person",
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "actor" });
        entity.Tags.Add(new() { TagName = "director" });
        _context.Subjects.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Subject?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Subject.Tags)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllSubjects()
    {
        // Arrange
        var entity1 = new SubjectEntity
        {
            Kind = "Person",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new SubjectEntity
        {
            Kind = "Studio",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Subjects.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Subject>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoSubjects()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Subject>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByKindAsync_ReturnsSubjects()
    {
        // Arrange
        var entity1 = new SubjectEntity
        {
            Kind = "Person",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new SubjectEntity
        {
            Kind = "Person",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new SubjectEntity
        {
            Kind = "Studio",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Subjects.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByKindAsync("Person");

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Subject>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByKindAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByKindAsync("NonExistent");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Subject>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByTagAsync_ReturnsSubjects()
    {
        // Arrange
        EnsureTagsExist("shared", "unique");
        var entity1 = new SubjectEntity
        {
            Kind = "Person",
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity1.Tags.Add(new() { TagName = "shared" });
        var entity2 = new SubjectEntity
        {
            Kind = "Person",
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity2.Tags.Add(new() { TagName = "shared" });
        var entity3 = new SubjectEntity
        {
            Kind = "Person",
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity3.Tags.Add(new() { TagName = "unique" });
        _context.Subjects.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByTagAsync("shared");

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Subject>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByTagAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByTagAsync("nonexistent");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Subject>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesSubject()
    {
        // Arrange
        var model = new Subject(
            Guid.Empty,
            "Person",
            null,
            new(Guid.Empty, DateTimeOffset.MinValue, []),
            [],
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from s in _context.Subjects.AsNoTracking()
            where s.Kind == "Person"
            select s).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Subject>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Subject.Kind)).EqualTo("Person"));
            Assert.That(entity, Is.InstanceOf<SubjectEntity>()
                .With.Property(nameof(SubjectEntity.Kind)).EqualTo("Person")
                .And.Property(nameof(SubjectEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Subject")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesSubjectWithTags()
    {
        // Arrange
        EnsureTagsExist("actor", "director", "producer");
        var model = new Subject(
            Guid.Empty,
            "Person",
            null,
            new(Guid.Empty, DateTimeOffset.MinValue, []),
            ["actor", "director", "producer"],
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from s in _context.Subjects.AsNoTracking().Include(s => s.Tags)
            where s.Kind == "Person"
            select s).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Subject>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Subject.Tags)).With.Count.EqualTo(3));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Tags, Has.Count.EqualTo(3));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesSubject()
    {
        // Arrange
        var entity = new SubjectEntity
        {
            Kind = "OriginalKind",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Subjects.Add(entity);
        _context.SaveChanges();

        var model = new Subject(
            entity.Id,
            "UpdatedKind",
            DateTimeOffset.UtcNow,
            new(Guid.Empty, DateTimeOffset.MinValue, []),
            [],
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from s in _context.Subjects.AsNoTracking()
            where s.Id == entity.Id
            select s).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<SubjectEntity>()
                .With.Property(nameof(SubjectEntity.Kind)).EqualTo("UpdatedKind")
                .And.Property(nameof(SubjectEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(SubjectEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Subject")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new Subject(
            Guid.NewGuid(),
            "NonExistent",
            null,
            new(Guid.Empty, DateTimeOffset.MinValue, []),
            [],
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateAsync_AddsAndRemovesTags()
    {
        // Arrange
        EnsureTagsExist("existing", "toRemove", "toAdd");
        var entity = new SubjectEntity
        {
            Kind = "Person",
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        entity.Tags.Add(new() { TagName = "toRemove" });
        _context.Subjects.Add(entity);
        _context.SaveChanges();

        var model = new Subject(
            entity.Id,
            entity.Kind,
            null,
            new(Guid.Empty, DateTimeOffset.MinValue, []),
            ["existing", "toAdd"],
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var tagEntities = await _context.SubjectTags
            .AsNoTracking()
            .Where(t => t.SubjectId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(SubjectTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(SubjectTagEntity.TagName)).EqualTo("toAdd"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(SubjectTagEntity.TagName)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesSubject()
    {
        // Arrange
        var entity = new SubjectEntity
        {
            Kind = "ToDelete",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Subjects.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.Subjects.AnyAsync(s => s.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Subject")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task DeleteAsync_CascadesToTags()
    {
        // Arrange
        EnsureTagsExist("tag1", "tag2");
        var entity = new SubjectEntity
        {
            Kind = "Person",
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "tag1" });
        entity.Tags.Add(new() { TagName = "tag2" });
        _context.Subjects.Add(entity);
        _context.SaveChanges();

        var subjectId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(subjectId, _userId);

        // Assert
        var tagsExist = await _context.SubjectTags
            .AnyAsync(t => t.SubjectId == subjectId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagsExist, Is.False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_AddAndRemove()
    {
        // Arrange
        EnsureTagsExist("existing", "toRemove", "toAdd");
        var entity = new SubjectEntity
        {
            Kind = "Person",
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        entity.Tags.Add(new() { TagName = "toRemove" });
        _context.Subjects.Add(entity);
        _context.SaveChanges();

        var patch = new SubjectPatch
        {
            Id = entity.Id,
            Tags = new(Add: ["toAdd"], Remove: ["toRemove"])
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.SubjectTags
            .AsNoTracking()
            .Where(t => t.SubjectId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(SubjectTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(SubjectTagEntity.TagName)).EqualTo("toAdd"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(SubjectTagEntity.TagName)).EqualTo("toRemove"));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Patch")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Subject")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new SubjectPatch
        {
            Id = Guid.NewGuid(),
            Tags = new(Add: ["tag"], Remove: null)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateLastRefreshedAsync_UpdatesTimestamp()
    {
        // Arrange
        var entity = new SubjectEntity
        {
            Kind = "Person",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Subjects.Add(entity);
        _context.SaveChanges();

        var refreshTime = DateTimeOffset.UtcNow;

        // Act
        var result = await _subject.UpdateLastRefreshedAsync(entity.Id, refreshTime);

        // Assert
        var updatedEntity = await _context.Subjects
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.Not.Null);
            Assert.That(updatedEntity!.DateLastRefreshed, Is.EqualTo(refreshTime).Within(TimeSpan.FromSeconds(1)));
        }
    }

    [Test]
    public async Task UpdateLastRefreshedAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.UpdateLastRefreshedAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }
}
