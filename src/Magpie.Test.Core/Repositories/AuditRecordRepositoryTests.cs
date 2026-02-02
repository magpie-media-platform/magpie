using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;
using Magpie.Models.Sorting;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class AuditRecordRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IAuditRecordRepository _subject = null!;
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
            Role = Models.Enums.Role.Admin,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.SaveChanges();

        _userId = user.Id;

        var loggerFactory = LoggerFactory.Create(builder => builder.AddTestLogger());

        _subject = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllRecords()
    {
        // Arrange
        var record1 = new AuditRecord { Category = "Security", Operation = "Login", Timestamp = DateTime.UtcNow };
        var record2 = new AuditRecord { Category = "Content", Operation = "Create", Timestamp = DateTime.UtcNow };
        var record3 = new AuditRecord { Category = "System", Operation = "Start", Timestamp = DateTime.UtcNow };
        _context.AuditRecords.AddRange(record1, record2, record3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Has.Exactly(3).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoRecords()
    {
        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListAllAsync_ReturnsRecordsOrderedByTimestampDescending()
    {
        // Arrange
        var oldRecord = new AuditRecord
        {
            Category = "System",
            Operation = "Start",
            Timestamp = DateTime.UtcNow.AddHours(-2)
        };
        var middleRecord = new AuditRecord
        {
            Category = "Security",
            Operation = "Login",
            Timestamp = DateTime.UtcNow.AddHours(-1)
        };
        var newRecord = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            Timestamp = DateTime.UtcNow
        };
        _context.AuditRecords.AddRange(oldRecord, middleRecord, newRecord);
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync(default)).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Exactly(3).Items);
            Assert.That(result[0], Has.Property(nameof(AuditRecord.Id)).EqualTo(newRecord.Id));
            Assert.That(result[1], Has.Property(nameof(AuditRecord.Id)).EqualTo(middleRecord.Id));
            Assert.That(result[2], Has.Property(nameof(AuditRecord.Id)).EqualTo(oldRecord.Id));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsRecord()
    {
        // Arrange
        var record = new AuditRecord
        {
            Category = "Security",
            Operation = "Login",
            EntityType = "User",
            EntityId = _userId.ToString(),
            Detail = "User logged in successfully",
            Timestamp = DateTime.UtcNow,
            UserId = _userId
        };
        _context.AuditRecords.Add(record);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(record.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Category)).EqualTo("Security"));
            Assert.That(result, Has.Property(nameof(result.Operation)).EqualTo("Login"));
            Assert.That(result, Has.Property(nameof(result.EntityType)).EqualTo("User"));
            Assert.That(result, Has.Property(nameof(result.Detail)).EqualTo("User logged in successfully"));
            Assert.That(result, Has.Property(nameof(result.UserId)).EqualTo(_userId));
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
    public async Task QueryAsync_ReturnsAllRecords_WhenNoFilters()
    {
        // Arrange
        var record1 = new AuditRecord { Category = "Security", Operation = "Login", Timestamp = DateTime.UtcNow };
        var record2 = new AuditRecord { Category = "Content", Operation = "Create", Timestamp = DateTime.UtcNow };
        _context.AuditRecords.AddRange(record1, record2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new AuditRecordQuery(), null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByEntityId()
    {
        // Arrange
        var entityId = Guid.NewGuid().ToString();
        var record1 = new AuditRecord
        {
            Category = "Content",
            Operation = "Update",
            EntityId = entityId,
            Timestamp = DateTime.UtcNow
        };
        var record2 = new AuditRecord
        {
            Category = "Content",
            Operation = "Delete",
            EntityId = entityId,
            Timestamp = DateTime.UtcNow
        };
        var record3 = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            EntityId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow
        };
        _context.AuditRecords.AddRange(record1, record2, record3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new AuditRecordQuery(EntityId: entityId), null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(AuditRecord.EntityId)).EqualTo(entityId));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByParentEntityId()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var record1 = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            ParentEntityId = parentId,
            Timestamp = DateTime.UtcNow
        };
        var record2 = new AuditRecord
        {
            Category = "Content",
            Operation = "Update",
            ParentEntityId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow
        };
        _context.AuditRecords.AddRange(record1, record2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new AuditRecordQuery(ParentEntityId: parentId), null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(AuditRecord.ParentEntityId)).EqualTo(parentId));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByEntityType()
    {
        // Arrange
        var record1 = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            EntityType = "Subject",
            Timestamp = DateTime.UtcNow
        };
        var record2 = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            EntityType = "Subject",
            Timestamp = DateTime.UtcNow
        };
        var record3 = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            EntityType = "Download",
            Timestamp = DateTime.UtcNow
        };
        _context.AuditRecords.AddRange(record1, record2, record3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new AuditRecordQuery(EntityType: "Subject"), null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(AuditRecord.EntityType)).EqualTo("Subject"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByCategory()
    {
        // Arrange
        var record1 = new AuditRecord { Category = "Security", Operation = "Login", Timestamp = DateTime.UtcNow };
        var record2 = new AuditRecord { Category = "Security", Operation = "Logout", Timestamp = DateTime.UtcNow };
        var record3 = new AuditRecord { Category = "Content", Operation = "Create", Timestamp = DateTime.UtcNow };
        _context.AuditRecords.AddRange(record1, record2, record3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new AuditRecordQuery(Category: "Security"), null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(AuditRecord.Category)).EqualTo("Security"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByOperation()
    {
        // Arrange
        var record1 = new AuditRecord { Category = "Content", Operation = "Create", Timestamp = DateTime.UtcNow };
        var record2 = new AuditRecord { Category = "System", Operation = "Create", Timestamp = DateTime.UtcNow };
        var record3 = new AuditRecord { Category = "Content", Operation = "Delete", Timestamp = DateTime.UtcNow };
        _context.AuditRecords.AddRange(record1, record2, record3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new AuditRecordQuery(Operation: "Create"), null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByDescription()
    {
        // Arrange
        var record1 = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            Detail = "Created new movie entry",
            Timestamp = DateTime.UtcNow
        };
        var record2 = new AuditRecord
        {
            Category = "Content",
            Operation = "Update",
            Detail = "Updated movie metadata",
            Timestamp = DateTime.UtcNow
        };
        var record3 = new AuditRecord
        {
            Category = "System",
            Operation = "Start",
            Detail = "System started",
            Timestamp = DateTime.UtcNow
        };
        _context.AuditRecords.AddRange(record1, record2, record3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new AuditRecordQuery(Description: "movie"), null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(AuditRecord.Detail)).Contains("movie"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByTimestampMin()
    {
        // Arrange
        var cutoff = DateTime.UtcNow.AddHours(-1);
        var oldRecord = new AuditRecord
        {
            Category = "System",
            Operation = "Start",
            Timestamp = DateTime.UtcNow.AddHours(-2)
        };
        var newRecord = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            Timestamp = DateTime.UtcNow
        };
        _context.AuditRecords.AddRange(oldRecord, newRecord);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new AuditRecordQuery(Timestamp: new Range<DateTime>(cutoff, null)),
            null,
            null,
            default);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(AuditRecord.Id)).EqualTo(newRecord.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByTimestampMax()
    {
        // Arrange
        var cutoff = DateTime.UtcNow.AddHours(-1);
        var oldRecord = new AuditRecord
        {
            Category = "System",
            Operation = "Start",
            Timestamp = DateTime.UtcNow.AddHours(-2)
        };
        var newRecord = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            Timestamp = DateTime.UtcNow
        };
        _context.AuditRecords.AddRange(oldRecord, newRecord);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new AuditRecordQuery(Timestamp: new Range<DateTime>(null, cutoff)),
            null,
            null,
            default);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(AuditRecord.Id)).EqualTo(oldRecord.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByTimestampRange()
    {
        // Arrange
        var minTime = DateTime.UtcNow.AddHours(-3);
        var maxTime = DateTime.UtcNow.AddHours(-1);
        var tooOld = new AuditRecord
        {
            Category = "System",
            Operation = "Start",
            Timestamp = DateTime.UtcNow.AddHours(-4)
        };
        var inRange = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            Timestamp = DateTime.UtcNow.AddHours(-2)
        };
        var tooNew = new AuditRecord
        {
            Category = "Security",
            Operation = "Login",
            Timestamp = DateTime.UtcNow
        };
        _context.AuditRecords.AddRange(tooOld, inRange, tooNew);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new AuditRecordQuery(Timestamp: new Range<DateTime>(minTime, maxTime)),
            null,
            null,
            default);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(AuditRecord.Id)).EqualTo(inRange.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByUserId()
    {
        // Arrange
        var otherUserId = Guid.NewGuid();
        var record1 = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            UserId = _userId,
            Timestamp = DateTime.UtcNow
        };
        var record2 = new AuditRecord
        {
            Category = "Content",
            Operation = "Update",
            UserId = _userId,
            Timestamp = DateTime.UtcNow
        };
        var record3 = new AuditRecord
        {
            Category = "Content",
            Operation = "Delete",
            UserId = otherUserId,
            Timestamp = DateTime.UtcNow
        };
        _context.AuditRecords.AddRange(record1, record2, record3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new AuditRecordQuery(UserId: _userId), null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task QueryAsync_CombinesMultipleFilters()
    {
        // Arrange
        var record1 = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            EntityType = "Subject",
            UserId = _userId,
            Timestamp = DateTime.UtcNow
        };
        var record2 = new AuditRecord
        {
            Category = "Content",
            Operation = "Update",
            EntityType = "Subject",
            UserId = _userId,
            Timestamp = DateTime.UtcNow
        };
        var record3 = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            EntityType = "Download",
            UserId = _userId,
            Timestamp = DateTime.UtcNow
        };
        var record4 = new AuditRecord
        {
            Category = "Security",
            Operation = "Create",
            EntityType = "Subject",
            UserId = _userId,
            Timestamp = DateTime.UtcNow
        };
        _context.AuditRecords.AddRange(record1, record2, record3, record4);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new AuditRecordQuery(Category: "Content", EntityType: "Subject"),
            null,
            null,
            default);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(AuditRecord.Category)).EqualTo("Content"));
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(AuditRecord.EntityType)).EqualTo("Subject"));
        }
    }

    [Test]
    public async Task QueryAsync_AppliesPaging()
    {
        // Arrange
        for (var i = 0; i < 10; i++)
        {
            _context.AuditRecords.Add(new AuditRecord
            {
                Category = "Content",
                Operation = $"Operation{i}",
                Timestamp = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new AuditRecordQuery(),
            new PagingParameter(2, 3),
            null,
            default);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(3).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(10));
            Assert.That(result, Has.Property(nameof(result.PageNumber)).EqualTo(2));
            Assert.That(result, Has.Property(nameof(result.PageSize)).EqualTo(3));
        }
    }

    [Test]
    public async Task QueryAsync_AppliesSortingAscending()
    {
        // Arrange
        var record1 = new AuditRecord { Category = "C", Operation = "Op1", Timestamp = DateTime.UtcNow };
        var record2 = new AuditRecord { Category = "A", Operation = "Op2", Timestamp = DateTime.UtcNow };
        var record3 = new AuditRecord { Category = "B", Operation = "Op3", Timestamp = DateTime.UtcNow };
        _context.AuditRecords.AddRange(record1, record2, record3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new AuditRecordQuery(),
            null,
            new SortParameter(nameof(AuditRecord.Category), SortDirection.Ascending),
            default);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items[0], Has.Property(nameof(AuditRecord.Category)).EqualTo("A"));
            Assert.That(items[1], Has.Property(nameof(AuditRecord.Category)).EqualTo("B"));
            Assert.That(items[2], Has.Property(nameof(AuditRecord.Category)).EqualTo("C"));
        }
    }

    [Test]
    public async Task QueryAsync_AppliesSortingDescending()
    {
        // Arrange
        var record1 = new AuditRecord { Category = "C", Operation = "Op1", Timestamp = DateTime.UtcNow };
        var record2 = new AuditRecord { Category = "A", Operation = "Op2", Timestamp = DateTime.UtcNow };
        var record3 = new AuditRecord { Category = "B", Operation = "Op3", Timestamp = DateTime.UtcNow };
        _context.AuditRecords.AddRange(record1, record2, record3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(
            new AuditRecordQuery(),
            null,
            new SortParameter(nameof(AuditRecord.Category), SortDirection.Descending),
            default);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items[0], Has.Property(nameof(AuditRecord.Category)).EqualTo("C"));
            Assert.That(items[1], Has.Property(nameof(AuditRecord.Category)).EqualTo("B"));
            Assert.That(items[2], Has.Property(nameof(AuditRecord.Category)).EqualTo("A"));
        }
    }

    [Test]
    public async Task QueryAsync_DefaultSortsbyTimestampDescending()
    {
        // Arrange
        var oldRecord = new AuditRecord
        {
            Category = "System",
            Operation = "Start",
            Timestamp = DateTime.UtcNow.AddHours(-2)
        };
        var newRecord = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            Timestamp = DateTime.UtcNow
        };
        _context.AuditRecords.AddRange(oldRecord, newRecord);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new AuditRecordQuery(), null, null);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items[0], Has.Property(nameof(AuditRecord.Id)).EqualTo(newRecord.Id));
            Assert.That(items[1], Has.Property(nameof(AuditRecord.Id)).EqualTo(oldRecord.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEmptyResult_WhenNoMatches()
    {
        // Act
        var result = await _subject.QueryAsync(
            new AuditRecordQuery(Category: "NonExistent"),
            null,
            null,
            default);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Empty);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(0));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesRecord()
    {
        // Arrange
        var record = new AuditRecord
        {
            Category = "Security",
            Operation = "Login",
            EntityType = "User",
            EntityId = _userId.ToString(),
            Detail = "User logged in successfully",
            UserId = _userId
        };

        // Act
        var result = await _subject.CreateAsync(record);

        // Assert
        var entity = await _context.AuditRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == result.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(entity, Has.Property(nameof(entity.Category)).EqualTo("Security"));
            Assert.That(entity, Has.Property(nameof(entity.Operation)).EqualTo("Login"));
            Assert.That(entity, Has.Property(nameof(entity.EntityType)).EqualTo("User"));
            Assert.That(entity, Has.Property(nameof(entity.Detail)).EqualTo("User logged in successfully"));
            Assert.That(entity, Has.Property(nameof(entity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_SetsTimestamp_WhenNotProvided()
    {
        // Arrange
        var beforeCreate = DateTime.UtcNow;
        var record = new AuditRecord
        {
            Category = "System",
            Operation = "Start"
        };

        // Act
        var result = await _subject.CreateAsync(record);
        var afterCreate = DateTime.UtcNow;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Timestamp)).GreaterThanOrEqualTo(beforeCreate));
            Assert.That(result, Has.Property(nameof(result.Timestamp)).LessThanOrEqualTo(afterCreate));
        }
    }

    [Test]
    public async Task CreateAsync_PreservesTimestamp_WhenProvided()
    {
        // Arrange
        var specificTimestamp = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var record = new AuditRecord
        {
            Category = "System",
            Operation = "Start",
            Timestamp = specificTimestamp
        };

        // Act
        var result = await _subject.CreateAsync(record);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Timestamp)).EqualTo(specificTimestamp));
    }

    [Test]
    public async Task CreateAsync_CreatesRecordWithAllFields()
    {
        // Arrange
        var entityId = Guid.NewGuid().ToString();
        var parentEntityId = Guid.NewGuid().ToString();
        var timestamp = DateTime.UtcNow;

        var record = new AuditRecord
        {
            Category = "Content",
            Operation = "Update",
            EntityId = entityId,
            ParentEntityId = parentEntityId,
            EntityType = "Subject",
            Detail = "Updated subject metadata",
            Timestamp = timestamp,
            UserId = _userId
        };

        // Act
        var result = await _subject.CreateAsync(record);

        // Assert
        var entity = await _context.AuditRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == result.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entity, Has.Property(nameof(entity.Category)).EqualTo("Content"));
            Assert.That(entity, Has.Property(nameof(entity.Operation)).EqualTo("Update"));
            Assert.That(entity, Has.Property(nameof(entity.EntityId)).EqualTo(entityId));
            Assert.That(entity, Has.Property(nameof(entity.ParentEntityId)).EqualTo(parentEntityId));
            Assert.That(entity, Has.Property(nameof(entity.EntityType)).EqualTo("Subject"));
            Assert.That(entity, Has.Property(nameof(entity.Detail)).EqualTo("Updated subject metadata"));
            Assert.That(entity, Has.Property(nameof(entity.Timestamp)).EqualTo(timestamp));
            Assert.That(entity, Has.Property(nameof(entity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesRecordWithNullOptionalFields()
    {
        // Arrange
        var record = new AuditRecord
        {
            Category = "System",
            Operation = "Startup"
        };

        // Act
        var result = await _subject.CreateAsync(record);

        // Assert
        var entity = await _context.AuditRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == result.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entity, Has.Property(nameof(entity.EntityId)).Null);
            Assert.That(entity, Has.Property(nameof(entity.ParentEntityId)).Null);
            Assert.That(entity, Has.Property(nameof(entity.EntityType)).Null);
            Assert.That(entity, Has.Property(nameof(entity.Detail)).Null);
            Assert.That(entity, Has.Property(nameof(entity.UserId)).Null);
        }
    }
}
