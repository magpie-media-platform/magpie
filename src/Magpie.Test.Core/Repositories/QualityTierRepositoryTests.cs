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
public class QualityTierRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IQualityTierRepository _subject = null!;
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

        var loggerFactory = LoggerFactory.Create(builder => builder.AddTestLogger());

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());

        var transactionManager = new TransactionManager(_context);

        _subject = new QualityTierRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<QualityTierRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsQualityTier_WhenExists()
    {
        // Arrange
        var qualityTier = new QualityTier
        {
            Name = "HD",
            Criteria = new()
            {
                And = [new()
                {
                    Items = new()
                    {
                        ["resolution"] = new() { Operator = CriteriaValueOperator.Equals, Values = ["1080p"] }
                    }
                }]
            },
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTiers.Add(qualityTier);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(qualityTier.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("HD"));
            Assert.That(result, Has.Property(nameof(result.Criteria)).Not.Null);
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
    public async Task ListAllAsync_ReturnsAllQualityTiers()
    {
        // Arrange
        var qualityTier1 = new QualityTier
        {
            Name = "HD",
            Criteria = new() { And = [] },
            CreatedAt = DateTime.UtcNow
        };
        var qualityTier2 = new QualityTier
        {
            Name = "4K",
            Criteria = new() { And = [] },
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTiers.AddRange(qualityTier1, qualityTier2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoQualityTiers()
    {
        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesQualityTier()
    {
        // Arrange
        var qualityTier = new QualityTier
        {
            Name = "SD",
            Criteria = new()
            {
                And = [new()
                {
                    Items = new()
                    {
                        ["resolution"] = new() { Operator = CriteriaValueOperator.Equals, Values = ["480p"] }
                    }
                }]
            }
        };

        // Act
        var result = await _subject.CreateAsync(qualityTier, _userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("SD"));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord()
    {
        // Arrange
        var qualityTier = new QualityTier
        {
            Name = "AuditedTier",
            Criteria = new() { And = [] }
        };

        // Act
        await _subject.CreateAsync(qualityTier, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("QualityTier"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesQualityTier_WhenExists()
    {
        // Arrange
        var qualityTier = new QualityTier
        {
            Name = "OriginalTier",
            Criteria = new() { And = [] },
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTiers.Add(qualityTier);
        await _context.SaveChangesAsync();

        qualityTier.Name = "UpdatedTier";
        qualityTier.Criteria = new()
        {
            And = [new()
            {
                Items = new()
                {
                    ["codec"] = new() { Operator = CriteriaValueOperator.Equals, Values = ["x265"] }
                }
            }]
        };

        // Act
        var result = await _subject.UpdateAsync(qualityTier, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(qualityTier.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Name)).EqualTo("UpdatedTier"));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var qualityTier = new QualityTier
        {
            Id = Guid.NewGuid(),
            Name = "NonExistent",
            Criteria = new() { And = [] }
        };

        // Act
        var result = await _subject.UpdateAsync(qualityTier, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord()
    {
        // Arrange
        var qualityTier = new QualityTier
        {
            Name = "ToUpdate",
            Criteria = new() { And = [] },
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTiers.Add(qualityTier);
        await _context.SaveChangesAsync();

        qualityTier.Name = "Updated";

        // Act
        await _subject.UpdateAsync(qualityTier, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("QualityTier"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesQualityTier_WhenExists()
    {
        // Arrange
        var qualityTier = new QualityTier
        {
            Name = "ToDelete",
            Criteria = new() { And = [] },
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTiers.Add(qualityTier);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(qualityTier.Id, _userId);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(qualityTier.Id);
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
        var qualityTier = new QualityTier
        {
            Name = "DeleteAudit",
            Criteria = new() { And = [] },
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTiers.Add(qualityTier);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync(qualityTier.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("QualityTier"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesName_WhenNamePatchProvided()
    {
        // Arrange
        var qualityTier = new QualityTier
        {
            Name = "OriginalName",
            Criteria = new() { And = [] },
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTiers.Add(qualityTier);
        await _context.SaveChangesAsync();

        var patch = new QualityTierPatch { Id = qualityTier.Id, Name = new() { Value = "PatchedName" } };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(qualityTier.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Has.Property(nameof(patched.Name)).EqualTo("PatchedName"));
            Assert.That(patched, Has.Property(nameof(patched.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(patched, Has.Property(nameof(patched.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var patch = new QualityTierPatch { Id = Guid.NewGuid(), Name = new() { Value = "NonExistent" } };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord()
    {
        // Arrange
        var qualityTier = new QualityTier
        {
            Name = "PatchAudit",
            Criteria = new() { And = [] },
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTiers.Add(qualityTier);
        await _context.SaveChangesAsync();

        var patch = new QualityTierPatch { Id = qualityTier.Id, Name = new() { Value = "Patched" } };

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("QualityTier"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_DoesNotCreateAuditRecord_WhenNotExists()
    {
        // Arrange
        var patch = new QualityTierPatch { Id = Guid.NewGuid(), Name = new() { Value = "NonExistent" } };

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        Assert.That(auditRecords, Is.Empty);
    }

    [Test]
    public async Task PatchAsync_PreservesUnpatchedFields()
    {
        // Arrange
        var qualityTier = new QualityTier
        {
            Name = "PreserveTest",
            Criteria = new()
            {
                And = [new()
                {
                    Items = new()
                    {
                        ["resolution"] = new() { Operator = CriteriaValueOperator.Equals, Values = ["720p"] }
                    }
                }]
            },
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTiers.Add(qualityTier);
        await _context.SaveChangesAsync();

        var patch = new QualityTierPatch { Id = qualityTier.Id, Name = new() { Value = "NewName" } };

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var patched = await _subject.GetByIdAsync(qualityTier.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Has.Property(nameof(patched.Name)).EqualTo("NewName"));
            Assert.That(patched, Has.Property(nameof(patched.Criteria))
                .With.Property(nameof(QualityTierCriteria.And)).With.Count.EqualTo(1));
        }
    }
}
