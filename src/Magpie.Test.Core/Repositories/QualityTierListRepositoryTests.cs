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
public class QualityTierListRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IQualityTierListRepository _subject = null!;
    private Guid _userId;
    private Guid _qualityTierId;

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

        var qualityTier = new QualityTier
        {
            Name = "1080p",
            Criteria = new QualityTierCriteria(),
            CreatedAt = DateTime.UtcNow
        };

        _context.QualityTiers.Add(qualityTier);
        _context.SaveChanges();

        _qualityTierId = qualityTier.Id;

        var loggerFactory = LoggerFactory.Create(builder => builder.AddTestLogger());

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());

        var transactionManager = new TransactionManager(_context);

        _subject = new QualityTierListRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<QualityTierListRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsQualityTierList_WhenExists()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "TestList",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(qualityTierList.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestList"));
            Assert.That(result, Has.Property(nameof(result.UpgradeAllowed)).True);
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
    public async Task GetByIdAsync_IncludesQualityTierRanks()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "ListWithRanks",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        var rank = new QualityTierListRank
        {
            QualityTierListId = qualityTierList.Id,
            QualityTierId = _qualityTierId,
            Ordinal = 1,
            IsCutoff = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierListRanks.Add(rank);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(qualityTierList.Id);

        // Assert
        var ranks = result!.QualityTierRanks.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ranks, Has.Exactly(1).Items);
            Assert.That(ranks, Has.One.With.Property(nameof(QualityTierListRank.Ordinal)).EqualTo(1));
            Assert.That(ranks, Has.One.With.Property(nameof(QualityTierListRank.IsCutoff)).True);
        }
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllQualityTierLists()
    {
        // Arrange
        var list1 = new QualityTierList
        {
            Name = "List1",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        var list2 = new QualityTierList
        {
            Name = "List2",
            UpgradeAllowed = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.AddRange(list1, list2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoQualityTierLists()
    {
        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListByTagAsync_ReturnsMatchingQualityTierLists()
    {
        // Arrange
        var list1 = new QualityTierList
        {
            Name = "TaggedList",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        var list2 = new QualityTierList
        {
            Name = "UntaggedList",
            UpgradeAllowed = false,
            CreatedAt = DateTime.UtcNow
        };
        var tag = new Tag { Name = "premium" };
        tag.QualityTierLists.Add(list1);

        _context.QualityTierLists.AddRange(list1, list2);
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListByTagAsync("premium");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Exactly(1).Items);
            Assert.That(result, Has.One.With.Property(nameof(QualityTierList.Name)).EqualTo("TaggedList"));
        }
    }

    [Test]
    public async Task ListByTagAsync_ReturnsEmptyList_WhenTagNotFound()
    {
        // Arrange
        var list = new QualityTierList
        {
            Name = "TestList",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(list);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListByTagAsync("nonexistent");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesQualityTierList()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "NewList",
            UpgradeAllowed = true
        };

        // Act
        var result = await _subject.CreateAsync(qualityTierList, _userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewList"));
            Assert.That(result, Has.Property(nameof(result.UpgradeAllowed)).True);
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "AuditedList",
            UpgradeAllowed = true
        };

        // Act
        await _subject.CreateAsync(qualityTierList, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("QualityTierList"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesQualityTierList_WhenExists()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "OriginalList",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        qualityTierList.Name = "UpdatedList";
        qualityTierList.UpgradeAllowed = false;

        // Act
        var result = await _subject.UpdateAsync(qualityTierList, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(qualityTierList.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Name)).EqualTo("UpdatedList"));
            Assert.That(updated, Has.Property(nameof(updated.UpgradeAllowed)).False);
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Id = Guid.NewGuid(),
            Name = "NonExistent",
            UpgradeAllowed = true
        };

        // Act
        var result = await _subject.UpdateAsync(qualityTierList, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "ToUpdate",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        qualityTierList.Name = "Updated";

        // Act
        await _subject.UpdateAsync(qualityTierList, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("QualityTierList"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesQualityTierList_WhenExists()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "ToDelete",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(qualityTierList.Id, _userId);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(qualityTierList.Id);
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
    public async Task DeleteAsync_DeletesAssociatedRanks()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "ListWithRanks",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        var rank = new QualityTierListRank
        {
            QualityTierListId = qualityTierList.Id,
            QualityTierId = _qualityTierId,
            Ordinal = 1,
            IsCutoff = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierListRanks.Add(rank);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(qualityTierList.Id, _userId);

        // Assert
        Assert.That(result, Is.True);

        var remainingRanks = await _context.QualityTierListRanks
            .Where(r => r.QualityTierListId == qualityTierList.Id)
            .ToListAsync();
        Assert.That(remainingRanks, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "DeleteAudit",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync(qualityTierList.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("QualityTierList"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesName_WhenProvided()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "OriginalName",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        var patch = new QualityTierListPatch
        {
            Id = qualityTierList.Id,
            Name = new() { Value = "PatchedName" }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(qualityTierList.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Has.Property(nameof(patched.Name)).EqualTo("PatchedName"));
            Assert.That(patched, Has.Property(nameof(patched.UpgradeAllowed)).True);
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesUpgradeAllowed_WhenProvided()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "TestList",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        var patch = new QualityTierListPatch
        {
            Id = qualityTierList.Id,
            UpgradeAllowed = new() { Value = false }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(qualityTierList.Id);
        Assert.That(patched, Has.Property(nameof(patched.UpgradeAllowed)).False);
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var patch = new QualityTierListPatch
        {
            Id = Guid.NewGuid(),
            Name = new() { Value = "NonExistent" }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_AddsRank_WhenProvided()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "ListForRanks",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        var patch = new QualityTierListPatch
        {
            Id = qualityTierList.Id,
            QualityTierRanks = new()
            {
                Add = [new() { QualityTierId = _qualityTierId, Ordinal = 1, IsCutoff = true }]
            }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(qualityTierList.Id);
        var patchedRanks = patched!.QualityTierRanks.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patchedRanks, Has.Exactly(1).Items);
            Assert.That(patchedRanks, Has.One.With.Property(nameof(QualityTierListRank.Ordinal)).EqualTo(1));
            Assert.That(patchedRanks, Has.One.With.Property(nameof(QualityTierListRank.IsCutoff)).True);
        }
    }

    [Test]
    public async Task PatchAsync_RemovesRank_WhenProvided()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "ListWithRank",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        var rank = new QualityTierListRank
        {
            QualityTierListId = qualityTierList.Id,
            QualityTierId = _qualityTierId,
            Ordinal = 1,
            IsCutoff = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierListRanks.Add(rank);
        await _context.SaveChangesAsync();

        var patch = new QualityTierListPatch
        {
            Id = qualityTierList.Id,
            QualityTierRanks = new()
            {
                Remove = [_qualityTierId]
            }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(qualityTierList.Id);
        Assert.That(patched, Has.Property(nameof(patched.QualityTierRanks)).Empty);
    }

    [Test]
    public async Task PatchAsync_UpdatesRank_WhenProvided()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "ListWithRank",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        var rank = new QualityTierListRank
        {
            QualityTierListId = qualityTierList.Id,
            QualityTierId = _qualityTierId,
            Ordinal = 1,
            IsCutoff = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierListRanks.Add(rank);
        await _context.SaveChangesAsync();

        var patch = new QualityTierListPatch
        {
            Id = qualityTierList.Id,
            QualityTierRanks = new()
            {
                Update = [new()
                {
                    QualityTierId = new() { Value = _qualityTierId },
                    Ordinal = new() { Value = 5 },
                    IsCutoff = new() { Value = true }
                }]
            }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(qualityTierList.Id);
        var patchedRanks = patched!.QualityTierRanks.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patchedRanks, Has.Exactly(1).Items);
            Assert.That(patchedRanks, Has.One.With.Property(nameof(QualityTierListRank.Ordinal)).EqualTo(5));
            Assert.That(patchedRanks, Has.One.With.Property(nameof(QualityTierListRank.IsCutoff)).True);
        }
    }

    [Test]
    public async Task PatchAsync_AddsTag_WhenProvided()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "ListForTags",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        var patch = new QualityTierListPatch
        {
            Id = qualityTierList.Id,
            Tags = new() { Add = ["premium"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var taggedLists = await _subject.ListByTagAsync("premium");
        Assert.That(taggedLists, Has.Exactly(1).Items);
    }

    [Test]
    public async Task PatchAsync_RemovesTag_WhenProvided()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "TaggedList",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        var tag = new Tag { Name = "toRemove" };
        tag.QualityTierLists.Add(qualityTierList);

        _context.QualityTierLists.Add(qualityTierList);
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();

        var patch = new QualityTierListPatch
        {
            Id = qualityTierList.Id,
            Tags = new() { Remove = ["toRemove"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var taggedLists = await _subject.ListByTagAsync("toRemove");
        Assert.That(taggedLists, Is.Empty);
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "PatchAudit",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        var patch = new QualityTierListPatch
        {
            Id = qualityTierList.Id,
            Name = new() { Value = "Patched" }
        };

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("QualityTierList"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesTagAuditRecords_WhenTagsModified()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "TagAuditList",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        var patch = new QualityTierListPatch
        {
            Id = qualityTierList.Id,
            Tags = new() { Add = ["tag1", "tag2"] }
        };

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(3));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.Exactly(2).With.Property(nameof(AuditRecord.Operation)).EqualTo("AddTag"));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesRankAuditRecords_WhenRanksModified()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "RankAuditList",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        var patch = new QualityTierListPatch
        {
            Id = qualityTierList.Id,
            QualityTierRanks = new()
            {
                Add = [new() { QualityTierId = _qualityTierId, Ordinal = 1, IsCutoff = true }]
            }
        };

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(2));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("AddRank"));
        }
    }

    [Test]
    public async Task PatchAsync_SetsModifiedFields()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Name = "ModifiedTest",
            UpgradeAllowed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        await _context.SaveChangesAsync();

        var patch = new QualityTierListPatch
        {
            Id = qualityTierList.Id,
            Name = new() { Value = "Modified" }
        };

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var patched = await _subject.GetByIdAsync(qualityTierList.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Has.Property(nameof(patched.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(patched, Has.Property(nameof(patched.ModifiedAt)).Not.Null);
        }
    }
}
