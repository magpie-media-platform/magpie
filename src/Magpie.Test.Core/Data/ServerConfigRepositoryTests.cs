using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using ServerConfigEntity = Magpie.Data.Database.Entities.ServerConfig;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class ServerConfigRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IServerConfigRepository _subject = null!;
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

        _subject = new ServerConfigRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<ServerConfigRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task GetAsync_ReturnsServerConfig()
    {
        // Arrange
        var entity = new ServerConfigEntity
        {
            Id = 1,
            DefaultLanguagePreference = "en-US",
            AutoUpdateEnabled = true,
            BackupSchedule = "0 0 * * *",
            BackupRetentionPeriod = TimeSpan.FromDays(7)
        };
        _context.ServerConfigs.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ServerConfig?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ServerConfig.DefaultLanguagePreference))
                .EqualTo("en-US")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(ServerConfig.AutoUpdateEnabled))
                .True
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(ServerConfig.BackupSchedule))
                .EqualTo("0 0 * * *")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(ServerConfig.BackupRetentionPeriod))
                .EqualTo(TimeSpan.FromDays(7)));
    }

    [Test]
    public async Task GetAsync_ReturnsNull_WhenNoConfig()
    {
        // Act
        var result = await _subject.GetAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ServerConfig?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task UpdateAsync_UpdatesServerConfig()
    {
        // Arrange
        var entity = new ServerConfigEntity
        {
            Id = 1,
            DefaultLanguagePreference = "en-US",
            AutoUpdateEnabled = false,
            BackupSchedule = "0 0 * * *",
            BackupRetentionPeriod = TimeSpan.FromDays(7)
        };
        _context.ServerConfigs.Add(entity);
        _context.SaveChanges();

        var model = new ServerConfig(
            "de-DE",
            true,
            "0 12 * * 0",
            TimeSpan.FromDays(14));

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from sc in _context.ServerConfigs.AsNoTracking()
            where sc.Id == 1
            select sc).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ServerConfigEntity>()
                .With.Property(nameof(ServerConfigEntity.DefaultLanguagePreference)).EqualTo("de-DE")
                .And.Property(nameof(ServerConfigEntity.AutoUpdateEnabled)).True
                .And.Property(nameof(ServerConfigEntity.BackupSchedule)).EqualTo("0 12 * * 0")
                .And.Property(nameof(ServerConfigEntity.BackupRetentionPeriod)).EqualTo(TimeSpan.FromDays(14)));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ServerConfig")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNoConfig()
    {
        // Arrange
        var model = new ServerConfig(
            "en-US",
            true,
            "0 0 * * *",
            TimeSpan.FromDays(7));

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task PatchAsync_PatchesDefaultLanguagePreference()
    {
        // Arrange
        var entity = new ServerConfigEntity
        {
            Id = 1,
            DefaultLanguagePreference = "en-US",
            AutoUpdateEnabled = true,
            BackupSchedule = "0 0 * * *",
            BackupRetentionPeriod = TimeSpan.FromDays(7)
        };
        _context.ServerConfigs.Add(entity);
        _context.SaveChanges();

        var patch = new ServerConfigPatch
        {
            DefaultLanguagePreference = new("fr-FR")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ServerConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(sc => sc.Id == 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ServerConfigEntity>()
                .With.Property(nameof(ServerConfigEntity.DefaultLanguagePreference)).EqualTo("fr-FR")
                .And.Property(nameof(ServerConfigEntity.AutoUpdateEnabled)).True
                .And.Property(nameof(ServerConfigEntity.BackupSchedule)).EqualTo("0 0 * * *")
                .And.Property(nameof(ServerConfigEntity.BackupRetentionPeriod)).EqualTo(TimeSpan.FromDays(7)));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Patch")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ServerConfig")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesAutoUpdateEnabled()
    {
        // Arrange
        var entity = new ServerConfigEntity
        {
            Id = 1,
            DefaultLanguagePreference = "en-US",
            AutoUpdateEnabled = false,
            BackupSchedule = "0 0 * * *",
            BackupRetentionPeriod = TimeSpan.FromDays(7)
        };
        _context.ServerConfigs.Add(entity);
        _context.SaveChanges();

        var patch = new ServerConfigPatch
        {
            AutoUpdateEnabled = new(true)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ServerConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(sc => sc.Id == 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ServerConfigEntity>()
                .With.Property(nameof(ServerConfigEntity.AutoUpdateEnabled)).True);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesBackupSchedule()
    {
        // Arrange
        var entity = new ServerConfigEntity
        {
            Id = 1,
            DefaultLanguagePreference = "en-US",
            AutoUpdateEnabled = true,
            BackupSchedule = "0 0 * * *",
            BackupRetentionPeriod = TimeSpan.FromDays(7)
        };
        _context.ServerConfigs.Add(entity);
        _context.SaveChanges();

        var patch = new ServerConfigPatch
        {
            BackupSchedule = new("0 6 * * 1")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ServerConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(sc => sc.Id == 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ServerConfigEntity>()
                .With.Property(nameof(ServerConfigEntity.BackupSchedule)).EqualTo("0 6 * * 1"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesBackupRetentionPeriod()
    {
        // Arrange
        var entity = new ServerConfigEntity
        {
            Id = 1,
            DefaultLanguagePreference = "en-US",
            AutoUpdateEnabled = true,
            BackupSchedule = "0 0 * * *",
            BackupRetentionPeriod = TimeSpan.FromDays(7)
        };
        _context.ServerConfigs.Add(entity);
        _context.SaveChanges();

        var patch = new ServerConfigPatch
        {
            BackupRetentionPeriod = new(TimeSpan.FromDays(30))
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ServerConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(sc => sc.Id == 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ServerConfigEntity>()
                .With.Property(nameof(ServerConfigEntity.BackupRetentionPeriod)).EqualTo(TimeSpan.FromDays(30)));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesMultipleFields()
    {
        // Arrange
        var entity = new ServerConfigEntity
        {
            Id = 1,
            DefaultLanguagePreference = "en-US",
            AutoUpdateEnabled = false,
            BackupSchedule = "0 0 * * *",
            BackupRetentionPeriod = TimeSpan.FromDays(7)
        };
        _context.ServerConfigs.Add(entity);
        _context.SaveChanges();

        var patch = new ServerConfigPatch
        {
            DefaultLanguagePreference = new("ja-JP"),
            AutoUpdateEnabled = new(true),
            BackupSchedule = new("0 3 * * *"),
            BackupRetentionPeriod = new(TimeSpan.FromDays(90))
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ServerConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(sc => sc.Id == 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ServerConfigEntity>()
                .With.Property(nameof(ServerConfigEntity.DefaultLanguagePreference)).EqualTo("ja-JP")
                .And.Property(nameof(ServerConfigEntity.AutoUpdateEnabled)).True
                .And.Property(nameof(ServerConfigEntity.BackupSchedule)).EqualTo("0 3 * * *")
                .And.Property(nameof(ServerConfigEntity.BackupRetentionPeriod)).EqualTo(TimeSpan.FromDays(90)));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNoConfig()
    {
        // Arrange
        var patch = new ServerConfigPatch
        {
            AutoUpdateEnabled = new(true)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task PatchAsync_ClearsDefaultLanguagePreference()
    {
        // Arrange
        var entity = new ServerConfigEntity
        {
            Id = 1,
            DefaultLanguagePreference = "en-US",
            AutoUpdateEnabled = true,
            BackupSchedule = "0 0 * * *",
            BackupRetentionPeriod = TimeSpan.FromDays(7)
        };
        _context.ServerConfigs.Add(entity);
        _context.SaveChanges();

        var patch = new ServerConfigPatch
        {
            DefaultLanguagePreference = new(null)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ServerConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(sc => sc.Id == 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ServerConfigEntity>()
                .With.Property(nameof(ServerConfigEntity.DefaultLanguagePreference)).Null);
        }
    }
}
