using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class BlockedFileTypeRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IBlockedFileTypeRepository _subject = null!;
    private List<MagpieEvent> _publishedEvents = null!;
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

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _publishedEvents = [];
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) => _publishedEvents.Add(magpieEvent))
            .Returns(Task.CompletedTask);

        _subject = new BlockedFileTypeRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            eventBus.Object,
            NullLogger<BlockedFileTypeRepository>.Instance,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task CreateAsync_CreatesBlockedFileType_WhenRequestIsValid()
    {
        // Arrange
        var model = new BlockedFileType
        {
            Pattern = ".iso",
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.BlockedFileTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == result.Id)
            .ConfigureAwait(false);
        var audit = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Pattern)).EqualTo(".iso"));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity, Has.Property(nameof(entity.Pattern)).EqualTo(".iso"));
            Assert.That(entity, Has.Property(nameof(entity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(audit, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllRows_WhenRowsExist()
    {
        // Arrange
        var now = DateTime.UtcNow;
        _context.BlockedFileTypes.AddRange(
            new BlockedFileType
            {
                Pattern = ".iso",
                CreatedAt = now
            },
            new BlockedFileType
            {
                Pattern = "application/x-msdownload",
                CreatedAt = now
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).ToList();

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoRows()
    {
        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task UpdateAsync_UpdatesBlockedFileType_WhenEntityExists()
    {
        // Arrange
        var entity = new BlockedFileType
        {
            Pattern = ".iso",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.BlockedFileTypes.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var model = new BlockedFileType
        {
            Id = entity.Id,
            Pattern = ".img",
            CreatedAt = entity.CreatedAt,
            CreatedByUserId = entity.CreatedByUserId
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var reloaded = await _context.BlockedFileTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entity.Id)
            .ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(reloaded, Has.Property(nameof(reloaded.Pattern)).EqualTo(".img"));
            Assert.That(reloaded, Has.Property(nameof(reloaded.ModifiedAt)).Not.Null);
            Assert.That(reloaded, Has.Property(nameof(reloaded.ModifiedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenRowDoesNotExist()
    {
        // Arrange
        var model = new BlockedFileType
        {
            Id = Guid.NewGuid(),
            Pattern = ".nope",
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(auditRecords, Is.Empty);
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesBlockedFileType_WhenEntityExists()
    {
        // Arrange
        var entity = new BlockedFileType
        {
            Pattern = ".iso",
            CreatedAt = DateTime.UtcNow
        };
        _context.BlockedFileTypes.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId).ConfigureAwait(false);

        // Assert
        var exists = await _context.BlockedFileTypes.AnyAsync(e => e.Id == entity.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(exists, Is.False);
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenRowDoesNotExist()
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
    public async Task CreateDeleteAsync_PublishesLifecycleEvents_WhenWritesSucceed()
    {
        // Arrange
        var model = new BlockedFileType { Pattern = ".lifecycle" };

        // Act
        var created = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);
        await _subject.DeleteAsync(created.Id, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _publishedEvents.Select(magpieEvent => magpieEvent.Name),
                Is.EqualTo(
                [
                    "blockedFileType.created",
                    "blockedFileType.deleted"
                ]));
            Assert.That(
                _publishedEvents,
                Has.All.Matches<MagpieEvent>(magpieEvent =>
                    magpieEvent.Context.Values.Contains(created.Id.ToString("D"), StringComparer.Ordinal)));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsRow_WhenExists()
    {
        // Arrange
        var entity = new BlockedFileType
        {
            Pattern = ".iso",
            CreatedAt = DateTime.UtcNow
        };
        _context.BlockedFileTypes.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(entity.Id).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Property(nameof(result.Pattern)).EqualTo(".iso"));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenRowDoesNotExist()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task CreateAsync_Throws_WhenDuplicatePatternAndType()
    {
        // Arrange
        var first = new BlockedFileType
        {
            Pattern = ".iso",
        };
        await _subject.CreateAsync(first, _userId).ConfigureAwait(false);

        // Act
        var duplicate = new BlockedFileType
        {
            Pattern = ".iso",
        };

        // Assert
        Assert.ThrowsAsync<DbUpdateException>(async () =>
            await _subject.CreateAsync(duplicate, _userId).ConfigureAwait(false));
    }
}
