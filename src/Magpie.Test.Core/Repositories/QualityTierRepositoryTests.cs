using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions;
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
public sealed class QualityTierRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IQualityTierRepository _subject = null!;
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
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .Options);

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

        _subject = new QualityTierRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<QualityTierRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(qualityTier.Id).ConfigureAwait(false);

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
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllQualityTiers_WhenTiersExist()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoQualityTiers()
    {
        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesQualityTier_WhenRequestIsValid()
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
        var result = await _subject.CreateAsync(qualityTier, _userId).ConfigureAwait(false);

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
    public async Task CreateAsync_CreatesAuditRecord_WhenTierIsCreated()
    {
        // Arrange
        var qualityTier = new QualityTier
        {
            Name = "AuditedTier",
            Criteria = new() { And = [] }
        };

        // Act
        await _subject.CreateAsync(qualityTier, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

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
        var result = await _subject.UpdateAsync(qualityTier, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(qualityTier.Id).ConfigureAwait(false);
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
        var result = await _subject.UpdateAsync(qualityTier, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord_WhenTierExists()
    {
        // Arrange
        var qualityTier = new QualityTier
        {
            Name = "ToUpdate",
            Criteria = new() { And = [] },
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTiers.Add(qualityTier);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        qualityTier.Name = "Updated";

        // Act
        await _subject.UpdateAsync(qualityTier, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(qualityTier.Id, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(qualityTier.Id).ConfigureAwait(false);
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord_WhenTierExists()
    {
        // Arrange
        var qualityTier = new QualityTier
        {
            Name = "DeleteAudit",
            Criteria = new() { And = [] },
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTiers.Add(qualityTier);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(qualityTier.Id, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new QualityTierPatch { Id = qualityTier.Id, Name = new() { Value = "PatchedName" } };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(qualityTier.Id).ConfigureAwait(false);
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
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord_WhenPatchSucceeds()
    {
        // Arrange
        var qualityTier = new QualityTier
        {
            Name = "PatchAudit",
            Criteria = new() { And = [] },
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTiers.Add(qualityTier);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new QualityTierPatch { Id = qualityTier.Id, Name = new() { Value = "Patched" } };

        // Act
        await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
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
        await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        Assert.That(auditRecords, Is.Empty);
    }

    [Test]
    public async Task PatchAsync_PreservesUnpatchedFields_WhenPatchIsPartial()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new QualityTierPatch { Id = qualityTier.Id, Name = new() { Value = "NewName" } };

        // Act
        await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var patched = await _subject.GetByIdAsync(qualityTier.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Has.Property(nameof(patched.Name)).EqualTo("NewName"));
            Assert.That(
                patched,
                Has.Property(nameof(patched.Criteria))
                    .With.Property(nameof(QualityTierCriteria.And)).With.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task PatchAsync_PersistsCriteria_WhenCriteriaPatchProvided()
    {
        // Arrange
        var qualityTier = new QualityTier
        {
            Name = "CriteriaTier",
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new QualityTierPatch
        {
            Id = qualityTier.Id,
            Criteria = new()
            {
                Value = new()
                {
                    And = [new()
                    {
                        Items = new()
                        {
                            ["resolution"] = new() { Operator = CriteriaValueOperator.Equals, Values = ["720p QA"] }
                        }
                    }]
                }
            }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(qualityTier.Id).ConfigureAwait(false);
        var values = patched!.Criteria.And.Single().Items["resolution"].Values;
        Assert.That(values, Is.EqualTo(["720p QA"]));
    }

    [Test]
    public async Task DeleteAsync_RejectsReferencedTier_WhenListRanksIt()
    {
        // Arrange
        var tier = new QualityTier { Name = "Ranked tier", Criteria = new() };
        var list = new QualityTierList { Name = "Test list" };
        list.QualityTierRanks.Add(new() { QualityTier = tier });
        _context.QualityTierLists.Add(list);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        Assert.ThrowsAsync<MagpieException>(() => _subject.DeleteAsync(tier.Id, _userId));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.QualityTiers.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await _context.QualityTierListRanks.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await _context.AuditRecords.CountAsync().ConfigureAwait(false), Is.Zero);
        }
    }
}
