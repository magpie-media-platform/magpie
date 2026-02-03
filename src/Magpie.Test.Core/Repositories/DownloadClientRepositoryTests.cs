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
public class DownloadClientRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IDownloadClientRepository _subject = null!;
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

        _subject = new DownloadClientRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<DownloadClientRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsDownloadClient_WhenExists()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "TestClient",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(client.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestClient"));
            Assert.That(result, Has.Property(nameof(result.ProviderType)).EqualTo("qbittorrent"));
            Assert.That(result, Has.Property(nameof(result.Enabled)).True);
            Assert.That(result, Has.Property(nameof(result.Status)).EqualTo(DownloadClientStatus.Healthy));
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
    public async Task ListAllAsync_ReturnsAllDownloadClients()
    {
        // Arrange
        var client1 = new DownloadClient
        {
            Name = "Client1",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        var client2 = new DownloadClient
        {
            Name = "Client2",
            ProviderType = "sabnzbd",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.AddRange(client1, client2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoDownloadClients()
    {
        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesDownloadClient()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "NewClient",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown
        };

        // Act
        var result = await _subject.CreateAsync(client, _userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewClient"));
            Assert.That(result, Has.Property(nameof(result.ProviderType)).EqualTo("qbittorrent"));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesDownloadClientWithSettings()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "ClientWithSettings",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            Settings = { ["host"] = "localhost", ["port"] = "8080" }
        };

        // Act
        var result = await _subject.CreateAsync(client, _userId);

        // Assert
        var persisted = await _context.DownloadClients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == result.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted, Has.Property(nameof(persisted.Settings)).With.Count.EqualTo(2));
            Assert.That(persisted, Has.Property(nameof(persisted.Settings)).ContainKey("host").WithValue("localhost"));
            Assert.That(persisted, Has.Property(nameof(persisted.Settings)).ContainKey("port").WithValue("8080"));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "AuditedClient",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown
        };

        // Act
        await _subject.CreateAsync(client, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("DownloadClient"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesDownloadClient_WhenExists()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "OriginalClient",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync();

        client.Name = "UpdatedClient";
        client.Enabled = false;
        client.Status = DownloadClientStatus.Healthy;

        // Act
        var result = await _subject.UpdateAsync(client, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(client.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Name)).EqualTo("UpdatedClient"));
            Assert.That(updated, Has.Property(nameof(updated.Enabled)).False);
            Assert.That(updated, Has.Property(nameof(updated.Status)).EqualTo(DownloadClientStatus.Healthy));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "NonExistent",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown
        };

        // Act
        var result = await _subject.UpdateAsync(client, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "ToUpdate",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync();

        // Act
        client.Name = "Updated";
        await _subject.UpdateAsync(client, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("DownloadClient"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesDownloadClient_WhenExists()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "ToDelete",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(client.Id, _userId);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(client.Id);
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
        var client = new DownloadClient
        {
            Name = "DeleteAudit",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync(client.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("DownloadClient"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesNameField()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "ToPatch",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync();

        var patch = new DownloadClientPatch { Id = client.Id, Name = new() { Value = "PatchedName" } };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var patched = await _subject.GetByIdAsync(client.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(patched, Has.Property(nameof(patched.Name)).EqualTo("PatchedName"));
            Assert.That(patched, Has.Property(nameof(patched.ModifiedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesEnabledField()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "ToPatchEnabled",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync();

        var patch = new DownloadClientPatch { Id = client.Id, Enabled = new() { Value = false } };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var patched = await _subject.GetByIdAsync(client.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(patched, Has.Property(nameof(patched.Enabled)).False);
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var patch = new DownloadClientPatch { Id = Guid.NewGuid(), Name = new() { Value = "Test" } };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "ToPatchAudit",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync();

        var patch = new DownloadClientPatch { Id = client.Id, Enabled = new() { Value = false } };

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("DownloadClient"));
        }
    }
}
