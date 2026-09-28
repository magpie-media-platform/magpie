using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
using Magpie.Models.Paging;
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
public sealed class AuditRecordRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private List<MagpieEvent> _publishedEvents = null!;
    private ITransactionManager _transactionManager = null!;
    private IAuditRecordRepository _subject = null!;
    private Guid _userId;
    private FakeTimeProvider _time = null!;

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
        _publishedEvents = [];
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) => _publishedEvents.Add(magpieEvent))
            .Returns(Task.CompletedTask);

        _time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        _transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _subject = new AuditRecordRepository(
            _context,
            eventBus.Object,
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            transactionManager: _transactionManager,
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: _time);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task CreateAsync_DefersEvent_WhenTheOuterTransactionIsOpen()
    {
        // Arrange
        var record = new AuditRecord { Category = "Security", Operation = "Login", Timestamp = DateTime.UtcNow };
        var eventsDuringTransaction = -1;

        // Act
        await _transactionManager.ExecuteWithTransactionAsync(async ct =>
        {
            await _subject.CreateAsync(record, ct).ConfigureAwait(false);
            eventsDuringTransaction = _publishedEvents.Count;
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(eventsDuringTransaction, Is.Zero);
            Assert.That(_publishedEvents, Has.Count.EqualTo(1));
            Assert.That(_publishedEvents[0].Name, Is.EqualTo("audit.record.created"));
        }
    }

    [Test]
    public void CreateAsync_DropsEvent_WhenWrappedTransactionRollsBack()
    {
        // Arrange
        var record = new AuditRecord { Category = "Security", Operation = "Login", Timestamp = DateTime.UtcNow };

        // Act
        var act = (AsyncTestDelegate)(async () =>
            await _transactionManager.ExecuteWithTransactionAsync(async ct =>
            {
                await _subject.CreateAsync(record, ct).ConfigureAwait(false);
                throw new InvalidOperationException("boom");
            }).ConfigureAwait(false));

        // Assert
        Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.That(_publishedEvents, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CommitsBothRecordsAndRunsSecondPublication_WhenFirstPublicationFails()
    {
        // Arrange
        var firstRecord = new AuditRecord { Category = "System", Operation = "First", Timestamp = DateTime.UtcNow };
        var secondRecord = new AuditRecord { Category = "System", Operation = "Second", Timestamp = DateTime.UtcNow };
        var eventBus = new Mock<IEventBus>();
        eventBus
            .SetupSequence(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("The first publication failed."))
            .Returns(Task.CompletedTask);
        IAuditRecordRepository subject = new AuditRecordRepository(
            _context,
            eventBus.Object,
            NullLogger<AuditRecordRepository>.Instance,
            _transactionManager,
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));

        // Act
        await _transactionManager.ExecuteWithTransactionAsync(async ct =>
        {
            await subject.CreateAsync(firstRecord, ct).ConfigureAwait(false);
            await subject.CreateAsync(secondRecord, ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        // Assert
        var records = await _context.AuditRecords.AsNoTracking().ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(records, Has.Count.EqualTo(2));
            Assert.That(records, Has.One.Property(nameof(AuditRecord.Id)).EqualTo(firstRecord.Id));
            Assert.That(records, Has.One.Property(nameof(AuditRecord.Id)).EqualTo(secondRecord.Id));
        }

        eventBus.Verify(
            bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        eventBus.Verify(
            bus => bus.PublishAsync(
                It.Is<MagpieEvent>(magpieEvent =>
                    magpieEvent.Context.ContainsKey("audit.recordId") &&
                    magpieEvent.Context["audit.recordId"] == secondRecord.Id.ToString("D")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllRecords_WhenRecordsExist()
    {
        // Arrange
        var record1 = new AuditRecord { Category = "Security", Operation = "Login", Timestamp = DateTime.UtcNow };
        var record2 = new AuditRecord { Category = "Content", Operation = "Create", Timestamp = DateTime.UtcNow };
        var record3 = new AuditRecord { Category = "System", Operation = "Start", Timestamp = DateTime.UtcNow };
        _context.AuditRecords.AddRange(record1, record2, record3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(3).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoRecords()
    {
        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListAllAsync_ReturnsRecordsOrderedByTimestampDescending_WhenRecordsExist()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync(default).ConfigureAwait(false)).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Exactly(3).Items);
            Assert.That(result, Has.ItemAt(0).Property(nameof(AuditRecord.Id)).EqualTo(newRecord.Id));
            Assert.That(result, Has.ItemAt(1).Property(nameof(AuditRecord.Id)).EqualTo(middleRecord.Id));
            Assert.That(result, Has.ItemAt(2).Property(nameof(AuditRecord.Id)).EqualTo(oldRecord.Id));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsRecord_WhenRecordExists()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(record.Id).ConfigureAwait(false);

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
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByEntityId_WhenEntityIdFilterIsSet()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { EntityId = entityId },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.All.Property(nameof(AuditRecord.EntityId)).EqualTo(entityId));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByParentEntityId_WhenParentEntityIdFilterIsSet()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { ParentEntityId = parentId },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.All.Property(nameof(AuditRecord.ParentEntityId)).EqualTo(parentId));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByEntityType_WhenEntityTypeFilterIsSet()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { EntityType = "Subject" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.All.Property(nameof(AuditRecord.EntityType)).EqualTo("Subject"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByCategory_WhenCategoryFilterIsSet()
    {
        // Arrange
        var record1 = new AuditRecord { Category = "Security", Operation = "Login", Timestamp = DateTime.UtcNow };
        var record2 = new AuditRecord { Category = "Security", Operation = "Logout", Timestamp = DateTime.UtcNow };
        var record3 = new AuditRecord { Category = "Content", Operation = "Create", Timestamp = DateTime.UtcNow };
        _context.AuditRecords.AddRange(record1, record2, record3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { Category = "Security" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.All.Property(nameof(AuditRecord.Category)).EqualTo("Security"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByOperation_WhenOperationFilterIsSet()
    {
        // Arrange
        var record1 = new AuditRecord { Category = "Content", Operation = "Create", Timestamp = DateTime.UtcNow };
        var record2 = new AuditRecord { Category = "System", Operation = "Create", Timestamp = DateTime.UtcNow };
        var record3 = new AuditRecord { Category = "Content", Operation = "Delete", Timestamp = DateTime.UtcNow };
        _context.AuditRecords.AddRange(record1, record2, record3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { Operation = "Create" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.All.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByDescription_WhenDescriptionFilterIsSet()
    {
        // Arrange
        var record1 = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            Detail = "Created new type-a entry",
            Timestamp = DateTime.UtcNow
        };
        var record2 = new AuditRecord
        {
            Category = "Content",
            Operation = "Update",
            Detail = "Updated type-a metadata",
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { Detail = "type-a" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.All.Property(nameof(AuditRecord.Detail)).Contains("type-a"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByTimestampMin_WhenMinimumTimestampIsSet()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { Timestamp = new(cutoff, null) },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.All.Property(nameof(AuditRecord.Id)).EqualTo(newRecord.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByTimestampMax_WhenMaximumTimestampIsSet()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { Timestamp = new(null, cutoff), },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.All.Property(nameof(AuditRecord.Id)).EqualTo(oldRecord.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByTimestampRange_WhenTimestampBoundsAreSet()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new() { Timestamp = new(minTime, maxTime) },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.All.Property(nameof(AuditRecord.Id)).EqualTo(inRange.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByCorrelationId_WhenCorrelationIdFilterIsSet()
    {
        // Arrange
        var targetCorrelation = Guid.NewGuid();
        var otherCorrelation = Guid.NewGuid();
        var match1 = new AuditRecord
        {
            Category = "Jobs",
            Operation = "Op1",
            CorrelationId = targetCorrelation,
            Timestamp = DateTime.UtcNow
        };
        var match2 = new AuditRecord
        {
            Category = "Jobs",
            Operation = "Op2",
            CorrelationId = targetCorrelation,
            Timestamp = DateTime.UtcNow
        };
        var differentChain = new AuditRecord
        {
            Category = "Jobs",
            Operation = "Op3",
            CorrelationId = otherCorrelation,
            Timestamp = DateTime.UtcNow
        };
        var noCorrelation = new AuditRecord
        {
            Category = "Jobs",
            Operation = "Op4",
            CorrelationId = null,
            Timestamp = DateTime.UtcNow
        };
        _context.AuditRecords.AddRange(match1, match2, differentChain, noCorrelation);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { CorrelationId = targetCorrelation },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.All.Property(nameof(AuditRecord.CorrelationId)).EqualTo(targetCorrelation));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByUserId_WhenUserIdFilterIsSet()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { UserId = _userId },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.All.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task QueryAsync_CombinesMultipleFilters_WhenMultipleFiltersAreSet()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { Category = "Content", EntityType = "Subject" },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.All.Property(nameof(AuditRecord.Category)).EqualTo("Content"));
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.All.Property(nameof(AuditRecord.EntityType)).EqualTo("Subject"));
        }
    }

    [Test]
    public async Task QueryAsync_AppliesPaging_WhenPagingIsSet()
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

        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new(), new() { PageNumber = 2, PageSize = 3 }, null)
            .ConfigureAwait(false);

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
    public async Task QueryAsync_ReturnsEmptySecondPage_WhenFirstPageExactlyMatchesPageSize()
    {
        // Arrange
        _context.AuditRecords.AddRange(
            new AuditRecord { Category = "Content", Operation = "First" },
            new AuditRecord { Category = "Content", Operation = "Second" },
            new AuditRecord { Category = "Content", Operation = "Third" });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var firstPage = await _subject.QueryAsync(new(), new() { PageNumber = 1, PageSize = 3 }, null)
            .ConfigureAwait(false);
        var secondPage = await _subject.QueryAsync(new(), new() { PageNumber = 2, PageSize = 3 }, null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstPage.Items, Has.Exactly(3).Items);
            Assert.That(firstPage.TotalItems, Is.EqualTo(3));
            Assert.That(firstPage.PageNumber, Is.EqualTo(1));
            Assert.That(firstPage.PageSize, Is.EqualTo(3));
            Assert.That(secondPage.Items, Is.Empty);
            Assert.That(secondPage.TotalItems, Is.EqualTo(3));
            Assert.That(secondPage.PageNumber, Is.EqualTo(2));
            Assert.That(secondPage.PageSize, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task QueryAsync_AppliesSortingAscending_WhenAscendingSortIsSet()
    {
        // Arrange
        var record1 = new AuditRecord { Category = "C", Operation = "Op1", Timestamp = DateTime.UtcNow };
        var record2 = new AuditRecord { Category = "A", Operation = "Op2", Timestamp = DateTime.UtcNow };
        var record3 = new AuditRecord { Category = "B", Operation = "Op3", Timestamp = DateTime.UtcNow };
        _context.AuditRecords.AddRange(record1, record2, record3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new(),
            new() { PageNumber = 1, PageSize = 100 },
            new()
            {
                PropertyName = nameof(AuditRecord.Category),
                Direction = SortDirection.Ascending
            }).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.ItemAt(0).Property(nameof(AuditRecord.Category)).EqualTo("A"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(AuditRecord.Category)).EqualTo("B"));
            Assert.That(items, Has.ItemAt(2).Property(nameof(AuditRecord.Category)).EqualTo("C"));
        }
    }

    [Test]
    public async Task QueryAsync_AppliesSortingDescending_WhenDescendingSortIsSet()
    {
        // Arrange
        var record1 = new AuditRecord { Category = "C", Operation = "Op1", Timestamp = DateTime.UtcNow };
        var record2 = new AuditRecord { Category = "A", Operation = "Op2", Timestamp = DateTime.UtcNow };
        var record3 = new AuditRecord { Category = "B", Operation = "Op3", Timestamp = DateTime.UtcNow };
        _context.AuditRecords.AddRange(record1, record2, record3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new(),
            new() { PageNumber = 1, PageSize = 100 },
            new()
            {
                PropertyName = nameof(AuditRecord.Category),
                Direction = SortDirection.Descending
            }).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.ItemAt(0).Property(nameof(AuditRecord.Category)).EqualTo("C"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(AuditRecord.Category)).EqualTo("B"));
            Assert.That(items, Has.ItemAt(2).Property(nameof(AuditRecord.Category)).EqualTo("A"));
        }
    }

    [Test]
    public async Task QueryAsync_DefaultSortsByTimestampDescending_WhenSortIsNotSet()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.ItemAt(0).Property(nameof(AuditRecord.Id)).EqualTo(newRecord.Id));
            Assert.That(items, Has.ItemAt(1).Property(nameof(AuditRecord.Id)).EqualTo(oldRecord.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEmptyResult_WhenNoMatches()
    {
        // Act
        var result = await _subject.QueryAsync(new() { Category = "NonExistent" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Empty);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(0));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesRecord_WhenRequestIsValid()
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
        var result = await _subject.CreateAsync(record).ConfigureAwait(false);

        // Assert
        var entity = await _context.AuditRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == result.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(entity, Has.Property(nameof(entity.Category)).EqualTo("Security"));
            Assert.That(entity, Has.Property(nameof(entity.Operation)).EqualTo("Login"));
            Assert.That(entity, Has.Property(nameof(entity.EntityType)).EqualTo("User"));
            Assert.That(entity, Has.Property(nameof(entity.Detail)).EqualTo("User logged in successfully"));
            Assert.That(entity, Has.Property(nameof(entity.UserId)).EqualTo(_userId));
            Assert.That(_publishedEvents, Has.Count.EqualTo(1));
            Assert.That(_publishedEvents[0].Name, Is.EqualTo("audit.record.created"));
            Assert.That(
                _publishedEvents[0].Context["audit.recordId"],
                Is.EqualTo(record.Id.ToString("D")));
        }
    }

    [Test]
    public void CreateAsync_DoesNotPublish_WhenPersistenceFails()
    {
        // Arrange
        _context.Database.GetDbConnection().Close();
        var record = new AuditRecord { Category = "System", Operation = "Startup" };

        // Act
        var action = (AsyncTestDelegate)(async () => await _subject.CreateAsync(record).ConfigureAwait(false));

        // Assert
        Assert.ThrowsAsync<DbUpdateException>(action);
        Assert.That(_publishedEvents, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_SetsTimestamp_WhenNotProvided()
    {
        // Arrange
        _time.SetUtcNow(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var record = new AuditRecord
        {
            Category = "System",
            Operation = "Start"
        };

        // Act
        var result = await _subject.CreateAsync(record).ConfigureAwait(false);

        // Assert
        Assert.That(result.Timestamp, Is.EqualTo(new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
    }

    [Test]
    public async Task CreateAsync_PreservesOtherChanges_WhenRejectedAuditIsRemoved()
    {
        // Arrange
        var committed = new AuditRecord { Category = "System", Operation = "Committed" };
        _context.AuditRecords.Add(committed);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var user = _context.Users.Local.Single();
        user.Username = "Pending update";
        var pendingUser = new User { Username = "Pending user", ProviderType = "local" };
        _context.Users.Add(pendingUser);
        var rejected = new AuditRecord { Category = "Integrations", Operation = "Import" };
        await _context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_import_audit BEFORE INSERT ON system_audit
            WHEN NEW.Operation = 'Import'
            BEGIN SELECT RAISE(ABORT, 'Import audit rejected'); END;
            """).ConfigureAwait(false);

        // Act
        var failure = Assert.ThrowsAsync<DbUpdateException>(async () =>
            await _subject.CreateAsync(rejected).ConfigureAwait(false));
        var rejectedState = _context.Entry(rejected).State;
        var pendingUserState = _context.Entry(pendingUser).State;
        var userState = _context.Entry(user).State;
        await _context.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_import_audit").ConfigureAwait(false);
        await _subject.CreateAsync(new() { Category = "Integrations", Operation = "ImportFailed" })
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.InnerException!.Message, Does.Contain("Import audit rejected"));
            Assert.That(rejectedState, Is.EqualTo(EntityState.Detached));
            Assert.That(pendingUserState, Is.EqualTo(EntityState.Added));
            Assert.That(userState, Is.EqualTo(EntityState.Modified));
            Assert.That(await _context.AuditRecords.AsNoTracking().AnyAsync(item => item.Id == rejected.Id)
                .ConfigureAwait(false), Is.False);
            Assert.That(await _context.AuditRecords.AsNoTracking().AnyAsync(item => item.Id == committed.Id)
                .ConfigureAwait(false), Is.True);
            Assert.That(await _context.AuditRecords.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await _context.Users.AsNoTracking().AnyAsync(item => item.Username == "Pending user")
                .ConfigureAwait(false), Is.True);
            Assert.That(await _context.Users.AsNoTracking().AnyAsync(item => item.Username == "Pending update")
                .ConfigureAwait(false), Is.True);
            Assert.That(_publishedEvents, Has.Count.EqualTo(1));
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
        var result = await _subject.CreateAsync(record).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Timestamp)).EqualTo(specificTimestamp));
    }

    [Test]
    public async Task CreateAsync_DetachesPendingAudit_WhenSaveIsCancelled()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);
        var rejected = new AuditRecord { Category = "Integrations", Operation = "Import" };

        // Act
        var failure = Assert.CatchAsync<OperationCanceledException>(async () =>
            await _subject.CreateAsync(rejected, cancellation.Token).ConfigureAwait(false));
        var rejectedState = _context.Entry(rejected).State;
        await _subject.CreateAsync(new() { Category = "Integrations", Operation = "Recovery" })
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(rejectedState, Is.EqualTo(EntityState.Detached));
            Assert.That(await _context.AuditRecords.AsNoTracking().AnyAsync(item => item.Id == rejected.Id)
                .ConfigureAwait(false), Is.False);
            Assert.That(await _context.AuditRecords.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(_publishedEvents, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task CreateAsync_PreservesCommittedAudit_WhenPublicationFails()
    {
        // Arrange
        var publicationError = new InvalidOperationException("Publication failed");
        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Throws(publicationError);
        IAuditRecordRepository subject = new AuditRecordRepository(
            _context, eventBus.Object, NullLogger<AuditRecordRepository>.Instance, _transactionManager,
            Mock.Of<IJobExecutionScopeFactory>(), _time);
        var committed = new AuditRecord { Category = "Integrations", Operation = "Import" };

        // Act
        var failure = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await subject.CreateAsync(committed).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure, Is.SameAs(publicationError));
            Assert.That(_context.Entry(committed).State, Is.EqualTo(EntityState.Unchanged));
            Assert.That(await _context.AuditRecords.AsNoTracking().AnyAsync(item => item.Id == committed.Id)
                .ConfigureAwait(false), Is.True);
        }
    }

    [Test]
    public async Task CreateAsync_RollsBackWritesAndEvents_WhenAuditIsRejectedInTransaction()
    {
        // Arrange
        await _context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_import_audit BEFORE INSERT ON system_audit
            WHEN NEW.Operation = 'Import'
            BEGIN SELECT RAISE(ABORT, 'Import audit rejected'); END;
            """).ConfigureAwait(false);

        // Act
        var failure = Assert.ThrowsAsync<DbUpdateException>(async () =>
            await _transactionManager.ExecuteWithTransactionAsync(async ct =>
            {
                await _context.Users.Where(user => user.Id == _userId)
                    .ExecuteUpdateAsync(update => update.SetProperty(user => user.Username, "Aborted"), ct)
                    .ConfigureAwait(false);
                await _subject.CreateAsync(new() { Category = "Integrations", Operation = "BeforeImport" }, ct)
                    .ConfigureAwait(false);
                await _subject.CreateAsync(new() { Category = "Integrations", Operation = "Import" }, ct)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false));
        var eventsAfterRollback = _publishedEvents.Count;
        var auditsAfterRollback = await _context.AuditRecords.CountAsync().ConfigureAwait(false);
        await _transactionManager.ExecuteWithTransactionAsync(async ct =>
            await _subject.CreateAsync(new() { Category = "Integrations", Operation = "ImportFailed" }, ct)
                .ConfigureAwait(false)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.InnerException!.Message, Does.Contain("Import audit rejected"));
            Assert.That(eventsAfterRollback, Is.Zero);
            Assert.That(auditsAfterRollback, Is.Zero);
            Assert.That(await _context.Users.AsNoTracking().Where(user => user.Id == _userId)
                .Select(user => user.Username).SingleAsync().ConfigureAwait(false), Is.EqualTo("TestUser"));
            Assert.That(await _context.AuditRecords.AsNoTracking().Select(record => record.Operation)
                .ToListAsync().ConfigureAwait(false), Is.EqualTo(["ImportFailed"]));
            Assert.That(_publishedEvents, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task CreateAsync_StampsAmbientJobExecutionId_WhenJobScopeIsActive()
    {
        // Arrange
        var scopes = new Mock<IJobExecutionScopeFactory>();
        scopes.SetupGet(factory => factory.CurrentExecutionId)
            .Returns(new Guid("5fcb4cc4-a34a-4d9b-ac06-3e5da7039d11"));
        IAuditRecordRepository subject = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            _transactionManager,
            scopes.Object,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        var record = new AuditRecord
        {
            Category = "Integrations",
            Operation = "Patch",
            EntityType = nameof(ContentHierarchyItem)
        };

        // Act
        var result = await subject.CreateAsync(record).ConfigureAwait(false);

        // Assert
        Assert.That(
            result,
            Has.Property(nameof(result.JobExecutionId))
                .EqualTo(new Guid("5fcb4cc4-a34a-4d9b-ac06-3e5da7039d11")));
    }

    [Test]
    public async Task CreateAsync_LeavesJobExecutionIdNull_WhenNoJobScopeIsActive()
    {
        // Arrange
        var scopes = new Mock<IJobExecutionScopeFactory>();
        scopes.SetupGet(factory => factory.CurrentExecutionId).Returns((Guid?)null);
        IAuditRecordRepository subject = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            _transactionManager,
            scopes.Object,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        var record = new AuditRecord
        {
            Category = "Integrations",
            Operation = "Patch",
            EntityType = nameof(ContentHierarchyItem)
        };

        // Act
        var result = await subject.CreateAsync(record).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(result.JobExecutionId)).Null);
    }

    [Test]
    public async Task CreateAsync_LeavesJobExecutionIdNull_WhenSubjectIsJobExecution()
    {
        // Arrange
        var scopes = new Mock<IJobExecutionScopeFactory>();
        scopes.SetupGet(factory => factory.CurrentExecutionId)
            .Returns(new Guid("ffd28534-8057-4ea1-9a21-b26620c2395c"));
        IAuditRecordRepository subject = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            _transactionManager,
            scopes.Object,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        var record = new AuditRecord
        {
            Category = "Jobs",
            Operation = "Create",
            EntityId = "9e8d4fda-01d5-4d8c-80f3-12c3b3d1bcbf",
            EntityType = nameof(JobExecution)
        };

        // Act
        var result = await subject.CreateAsync(record).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(result.JobExecutionId)).Null);
    }

    [TestCase(nameof(ContentHierarchyItem))]
    [TestCase(nameof(JobExecution))]
    public async Task CreateAsync_PreservesJobExecutionId_WhenCallerSetsIt(string entityType)
    {
        // Arrange
        var scopes = new Mock<IJobExecutionScopeFactory>();
        scopes.SetupGet(factory => factory.CurrentExecutionId)
            .Returns(new Guid("ffd28534-8057-4ea1-9a21-b26620c2395c"));
        IAuditRecordRepository subject = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            _transactionManager,
            scopes.Object,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        var record = new AuditRecord
        {
            Category = "Integrations",
            Operation = "Patch",
            EntityType = entityType,
            JobExecutionId = new Guid("73bd312b-3499-4eff-92f1-80d5b9a815c2")
        };

        // Act
        var result = await subject.CreateAsync(record).ConfigureAwait(false);

        // Assert
        Assert.That(
            result,
            Has.Property(nameof(result.JobExecutionId))
                .EqualTo(new Guid("73bd312b-3499-4eff-92f1-80d5b9a815c2")));
    }

    [Test]
    public async Task QueryAsync_FiltersByJobExecutionId_WhenJobExecutionIdFilterIsSet()
    {
        // Arrange
        var match = new AuditRecord
        {
            Category = "Integrations",
            Operation = "Patch",
            Timestamp = DateTime.UtcNow,
            JobExecutionId = new Guid("5fcb4cc4-a34a-4d9b-ac06-3e5da7039d11")
        };
        var otherExecution = new AuditRecord
        {
            Category = "Integrations",
            Operation = "Patch",
            Timestamp = DateTime.UtcNow,
            JobExecutionId = new Guid("ffd28534-8057-4ea1-9a21-b26620c2395c")
        };
        var noExecution = new AuditRecord
        {
            Category = "Integrations",
            Operation = "Patch",
            Timestamp = DateTime.UtcNow
        };
        _context.AuditRecords.AddRange(match, otherExecution, noExecution);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new() { JobExecutionId = new Guid("5fcb4cc4-a34a-4d9b-ac06-3e5da7039d11") },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.All.Property(nameof(AuditRecord.JobExecutionId))
                    .EqualTo(new Guid("5fcb4cc4-a34a-4d9b-ac06-3e5da7039d11")));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesRecordWithAllFields_WhenAllFieldsAreSet()
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
        var result = await _subject.CreateAsync(record).ConfigureAwait(false);

        // Assert
        var entity = await _context.AuditRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == result.Id).ConfigureAwait(false);

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
    public async Task CreateAsync_CreatesRecordWithNullOptionalFields_WhenOptionalFieldsAreNull()
    {
        // Arrange
        var record = new AuditRecord
        {
            Category = "System",
            Operation = "Startup"
        };

        // Act
        var result = await _subject.CreateAsync(record).ConfigureAwait(false);

        // Assert
        var entity = await _context.AuditRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == result.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entity, Has.Property(nameof(entity.EntityId)).Null);
            Assert.That(entity, Has.Property(nameof(entity.ParentEntityId)).Null);
            Assert.That(entity, Has.Property(nameof(entity.EntityType)).Null);
            Assert.That(entity, Has.Property(nameof(entity.Detail)).Null);
            Assert.That(entity, Has.Property(nameof(entity.UserId)).Null);
        }
    }

    [Test]
    public async Task QueryAsync_SortsByCamelCasePropertyName_WhenCamelCaseSortPropertyIsSet()
    {
        // Arrange
        var older = new AuditRecord
        {
            Category = "Content",
            Operation = "Create",
            Timestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var newer = new AuditRecord
        {
            Category = "Content",
            Operation = "Update",
            Timestamp = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        _context.AuditRecords.AddRange(older, newer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "timestamp", Direction = SortDirection.Descending };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items, Has.ItemAt(0).Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
        }
    }

    [Test]
    public async Task DeleteWhereAsync_DeletesAgedRecords_WhenRecordsHaveDifferentAges()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var agedTime = new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(-91));
        var agedRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLoggerFactory.Instance.CreateLogger<AuditRecordRepository>(),
            _transactionManager,
            Mock.Of<IJobExecutionScopeFactory>(),
            agedTime);
        var aged = await agedRepository.CreateAsync(
            new AuditRecord { Category = "test", Operation = "noop" },
            default).ConfigureAwait(false);
        var fresh = await _subject.CreateAsync(new AuditRecord { Category = "test", Operation = "noop" })
            .ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteWhereAsync(new AuditRecordQuery
        {
            TimestampBefore = now.AddDays(-90)
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(await _context.AuditRecords.AnyAsync(a => a.Id == aged.Id).ConfigureAwait(false), Is.False);
            Assert.That(await _context.AuditRecords.AnyAsync(a => a.Id == fresh.Id).ConfigureAwait(false), Is.True);
        }
    }

    [Test]
    public async Task DeleteWhereAsync_PreservesOtherRecords_WhenCategoryIsSpecifiedWithoutCutoff()
    {
        // Arrange
        _context.AuditRecords.AddRange(
            new AuditRecord { Category = "selected", Operation = "Create" },
            new AuditRecord { Category = "retained", Operation = "Create" });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteWhereAsync(new() { Category = "selected" }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.EqualTo(1));
            Assert.That((await _context.AuditRecords.SingleAsync().ConfigureAwait(false)).Category,
                Is.EqualTo("retained"));
        }
    }

    [TestCase(null, "499555cc-e7b2-4716-9f25-219c23624f85")]
    [TestCase("ac1a8e66-5577-41e9-ae46-3f586b385967", "ac1a8e66-5577-41e9-ae46-3f586b385967")]
    public async Task CreateAsync_PreservesCorrelation_WhenAmbientScopeExists(string? supplied, string expected)
    {
        // Arrange
        var scopes = new Mock<IJobExecutionScopeFactory>();
        scopes.SetupGet(scope => scope.CurrentCorrelationId).Returns(new Guid("499555cc-e7b2-4716-9f25-219c23624f85"));
        var repository = new AuditRecordRepository(_context, Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance, _transactionManager, scopes.Object, new FakeTimeProvider());
        var record = new AuditRecord
        {
            Category = "test",
            Operation = "Create",
            CorrelationId = supplied == null ? null : Guid.Parse(supplied)
        };

        // Act
        await repository.CreateAsync(record, default).ConfigureAwait(false);

        // Assert
        Assert.That((await _context.AuditRecords.SingleAsync().ConfigureAwait(false)).CorrelationId.ToString(),
            Is.EqualTo(expected));
    }
}
