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
public class SubjectRepositoryTests
{
    private MagpieDbContext _context = null!;
    private ISubjectRepository _subject = null!;
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

        _subject = new SubjectRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<SubjectRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsSubject_WhenExists()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(subject.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).EqualTo(subject.Id));
            Assert.That(result, Has.Property(nameof(result.Kind)).EqualTo("person"));
            Assert.That(result, Has.Property(nameof(result.MetadataContainerId)).EqualTo(_metadataContainerId));
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
    public async Task ListAllAsync_ReturnsAllSubjects()
    {
        // Arrange
        var subject1 = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        var metadataContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(metadataContainer2);
        await _context.SaveChangesAsync();

        var subject2 = new Subject
        {
            MetadataContainerId = metadataContainer2.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.AddRange(subject1, subject2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoSubjects()
    {
        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetByKindAsync_ReturnsSubjectsOfKind()
    {
        // Arrange
        var subject1 = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        var metadataContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var metadataContainer3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(metadataContainer2, metadataContainer3);
        await _context.SaveChangesAsync();

        var subject2 = new Subject
        {
            MetadataContainerId = metadataContainer2.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        var subject3 = new Subject
        {
            MetadataContainerId = metadataContainer3.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.AddRange(subject1, subject2, subject3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByKindAsync("person");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Exactly(2).Items);
            Assert.That(result, Has.All.Matches<Subject>(s => s.Kind == "person"));
        }
    }

    [Test]
    public async Task GetByKindAsync_ReturnsEmptyList_WhenNoSubjectsOfKind()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByKindAsync("studio");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetByTagAsync_ReturnsEmptyList()
    {
        // Act
        var result = await _subject.GetByTagAsync("testtag");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesSubject()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person"
        };

        // Act
        var result = await _subject.CreateAsync(subject, _userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Kind)).EqualTo("person"));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "studio"
        };

        // Act
        await _subject.CreateAsync(subject, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Subject"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesSubject_WhenExists()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync();

        subject.Kind = "studio";
        subject.DateLastRefreshed = DateTime.UtcNow;

        // Act
        var result = await _subject.UpdateAsync(subject, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(subject.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Kind)).EqualTo("studio"));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var subject = new Subject
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = _metadataContainerId,
            Kind = "person"
        };

        // Act
        var result = await _subject.UpdateAsync(subject, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync();

        subject.Kind = "studio";

        // Act
        await _subject.UpdateAsync(subject, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Subject"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesSubject_WhenExists()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(subject.Id, _userId);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(subject.Id);
        Assert.That(deleted, Is.Null);
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
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync(subject.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Subject"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesSubject_WhenExists()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync();

        var patch = new TagsPatch { Id = subject.Id };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(subject.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var patch = new TagsPatch { Id = Guid.NewGuid() };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync();

        var patch = new TagsPatch { Id = subject.Id };

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Subject"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateLastRefreshedAsync_UpdatesDateLastRefreshed_WhenExists()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync();

        var newRefreshDate = DateTime.UtcNow;

        // Act
        var result = await _subject.UpdateLastRefreshedAsync(subject.Id, newRefreshDate);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(subject.Id);
        Assert.That(updated, Has.Property(nameof(updated.DateLastRefreshed)).EqualTo(newRefreshDate));
    }

    [Test]
    public async Task UpdateLastRefreshedAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _subject.UpdateLastRefreshedAsync(Guid.NewGuid(), DateTime.UtcNow);

        // Assert
        Assert.That(result, Is.False);
    }
}
