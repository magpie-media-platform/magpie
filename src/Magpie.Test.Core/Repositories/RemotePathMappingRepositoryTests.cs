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
using Magpie.Models.Arguments;
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
public sealed class RemotePathMappingRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IRemotePathMappingRepository _subject = null!;
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

        _subject = new RemotePathMappingRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            eventBus.Object,
            NullLogger<RemotePathMappingRepository>.Instance,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task PatchAsync_LeavesUnnamedColumnsUnchanged_WhenOnlyTheLocalPathChanges()
    {
        // Arrange
        var mapping = new RemotePathMapping
        {
            Host = "seedbox",
            RemotePath = "/downloads",
            LocalPath = "/mnt/old",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = _userId
        };
        _context.RemotePathMappings.Add(mapping);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject.PatchAsync(
            new RemotePathMappingPatch
            {
                Id = mapping.Id,
                LocalPath = new() { Value = "/mnt/new" }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var stored = await _context.RemotePathMappings.AsNoTracking().SingleAsync().ConfigureAwait(false);
        var published = _publishedEvents.Single(magpieEvent =>
            magpieEvent.Name == "remotePathMapping.updated");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(stored.LocalPath, Is.EqualTo("/mnt/new"));
            Assert.That(stored.Host, Is.EqualTo("seedbox"));
            Assert.That(stored.RemotePath, Is.EqualTo("/downloads"));
            Assert.That(stored.ModifiedByUserId, Is.EqualTo(_userId));
            Assert.That(
                published.Context["remotePathMapping.changedFields"],
                Is.EqualTo("localPath"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenTheMappingDoesNotExist()
    {
        // Act
        var result = await _subject.PatchAsync(
            new RemotePathMappingPatch
            {
                Id = Guid.NewGuid(),
                Host = new() { Value = "seedbox" }
            },
            _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(_publishedEvents, Is.Empty);
        }
    }
}
