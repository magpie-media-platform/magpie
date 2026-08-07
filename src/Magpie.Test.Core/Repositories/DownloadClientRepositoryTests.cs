using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Core.Security;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class DownloadClientRepositoryTests
{
    private MagpieDbContext _context = null!;
    private string _dataRoot = null!;
    private IDownloadClientRepository _encryptedSubject = null!;
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

        var loggerFactory = NullLoggerFactory.Instance;

        _dataRoot = Path.Combine(Path.GetTempPath(), "magpie-secret-repo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);
        var hostEnvironment = new MagpieHostEnvironment(
            NodeId: "test-node",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: _dataRoot,
            ExtensionsRoot: _dataRoot,
            ConfigRoot: _dataRoot,
            DataRoot: _dataRoot,
            StartedAt: DateTime.UtcNow);
        var keyStore = new AtomicFileKeyStore(
            hostEnvironment,
            loggerFactory.CreateLogger<AtomicFileKeyStore>());
        var dataProtectionProvider = new DefaultDataProtectionProvider(
            keyStore,
            loggerFactory.CreateLogger<DefaultDataProtectionProvider>());
        var encryptionProtector = new DataProtectionService(dataProtectionProvider);

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance));

        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        var protector = new Mock<IDataProtectionService>();
        protector
            .Setup(p => p.Protect(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns((IReadOnlyDictionary<string, string> settings) => new Dictionary<string, string>(settings));
        protector
            .Setup(p => p.Unprotect(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns((IReadOnlyDictionary<string, string> stored) => new Dictionary<string, string>(stored));

        _subject = new DownloadClientRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            Mock.Of<IEventBus>(),
            loggerFactory.CreateLogger<DownloadClientRepository>(),
            protector.Object);

        _encryptedSubject = new DownloadClientRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            Mock.Of<IEventBus>(),
            loggerFactory.CreateLogger<DownloadClientRepository>(),
            encryptionProtector);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsDownloadClient_WhenExists()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "TestClient",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(client.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestClient"));
            Assert.That(result, Has.Property(nameof(result.ProviderType)).EqualTo("downloadclient-a"));
            Assert.That(result, Has.Property(nameof(result.Enabled)).True);
            Assert.That(result, Has.Property(nameof(result.Status)).EqualTo(IntegrationHealthStatus.Healthy));
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
    public async Task ListAllAsync_ReturnsAllDownloadClients_WhenClientsExist()
    {
        // Arrange
        var client1 = new DownloadClient
        {
            Name = "Client1",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        var client2 = new DownloadClient
        {
            Name = "Client2",
            ProviderType = "sabnzbd",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.AddRange(client1, client2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoDownloadClients()
    {
        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesDownloadClient_WhenRequestIsValid()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "NewClient",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown
        };

        // Act
        var result = await _subject.CreateAsync(client, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewClient"));
            Assert.That(result, Has.Property(nameof(result.ProviderType)).EqualTo("downloadclient-a"));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesDownloadClientWithSettings_WhenSettingsAreProvided()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "ClientWithSettings",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            Settings = { ["host"] = "localhost", ["port"] = "8080" }
        };

        // Act
        var result = await _subject.CreateAsync(client, _userId).ConfigureAwait(false);

        // Assert
        var persisted = await _context.DownloadClients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == result.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted, Has.Property(nameof(persisted.Settings)).With.Count.EqualTo(2));
            Assert.That(persisted, Has.Property(nameof(persisted.Settings)).ContainKey("host").WithValue("localhost"));
            Assert.That(persisted, Has.Property(nameof(persisted.Settings)).ContainKey("port").WithValue("8080"));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord_WhenClientIsCreated()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "AuditedClient",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown
        };

        // Act
        await _subject.CreateAsync(client, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
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
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        client.Name = "UpdatedClient";
        client.Enabled = false;
        client.Status = IntegrationHealthStatus.Healthy;

        // Act
        var result = await _subject.UpdateAsync(client, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(client.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Name)).EqualTo("UpdatedClient"));
            Assert.That(updated, Has.Property(nameof(updated.Enabled)).False);
            Assert.That(updated, Has.Property(nameof(updated.Status)).EqualTo(IntegrationHealthStatus.Healthy));
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
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown
        };

        // Act
        var result = await _subject.UpdateAsync(client, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord_WhenClientIsUpdated()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "ToUpdate",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        client.Name = "Updated";
        await _subject.UpdateAsync(client, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
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
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(client.Id, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(client.Id).ConfigureAwait(false);
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
    public async Task DeleteAsync_CreatesAuditRecord_WhenClientIsDeleted()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "DeleteAudit",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(client.Id, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("DownloadClient"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesNameField_WhenNameIsSet()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "ToPatch",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new DownloadClientPatch { Id = client.Id, Name = new() { Value = "PatchedName" } };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var patched = await _subject.GetByIdAsync(client.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(patched, Has.Property(nameof(patched.Name)).EqualTo("PatchedName"));
            Assert.That(patched, Has.Property(nameof(patched.ModifiedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesEnabledField_WhenEnabledIsSet()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "ToPatchEnabled",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new DownloadClientPatch { Id = client.Id, Enabled = new() { Value = false } };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var patched = await _subject.GetByIdAsync(client.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(patched, Has.Property(nameof(patched.Enabled)).False);
        }
    }

    [Test]
    public async Task CreateAsync_RoundTripsCleanupFields_WhenCleanupFieldsAreProvided()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "CleanupRoundTrip",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CleanupDeleteFiles = false,
            KeepFailedOnClient = true,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _subject.CreateAsync(client, _userId).ConfigureAwait(false);

        // Assert
        var stored = await _subject.GetByIdAsync(client.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.CleanupDeleteFiles, Is.False);
            Assert.That(stored.KeepFailedOnClient, Is.True);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesCleanupFields_WhenCleanupFieldsAreSet()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "CleanupPatch",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new DownloadClientPatch
        {
            Id = client.Id,
            CleanupDeleteFiles = new() { Value = false },
            KeepFailedOnClient = new() { Value = true }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var patched = await _subject.GetByIdAsync(client.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(patched!.CleanupDeleteFiles, Is.False);
            Assert.That(patched.KeepFailedOnClient, Is.True);
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var patch = new DownloadClientPatch { Id = Guid.NewGuid(), Name = new() { Value = "Test" } };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord_WhenClientIsPatched()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "ToPatchAudit",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new DownloadClientPatch { Id = client.Id, Enabled = new() { Value = false } };

        // Act
        await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("DownloadClient"));
        }
    }

    [Test]
    public async Task CreateAsync_StoresCiphertextAtRestButReturnsPlaintext_WhenSettingsHaveSecrets()
    {
        // Arrange
        var model = new DownloadClient
        {
            Name = "client",
            ProviderType = "downloadclient-a",
            Settings = new Dictionary<string, string> { ["password"] = "hunter2", ["host"] = "http://x" }
        };

        // Act
        var created = await _encryptedSubject.CreateAsync(model, null).ConfigureAwait(false);
        var storedSettings = await _context.DownloadClients
            .AsNoTracking()
            .Where(c => c.Id == created.Id)
            .Select(c => c.Settings)
            .FirstAsync().ConfigureAwait(false);
        var roundTripped = await _encryptedSubject.GetByIdAsync(created.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Settings["password"], Is.EqualTo("hunter2"));
            Assert.That(storedSettings.ContainsKey("__magpieSecret"), Is.True);
            Assert.That(storedSettings.Values.Any(v => v.Contains("hunter2", StringComparison.Ordinal)), Is.False);
            Assert.That(roundTripped!.Settings["password"], Is.EqualTo("hunter2"));
            Assert.That(roundTripped.Settings["host"], Is.EqualTo("http://x"));
        }
    }
}
