using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class MediaServerRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IMediaServerRepository _subject = null!;
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

        _subject = new MediaServerRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<MediaServerRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsMediaServer_WhenExists()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Name = "TestServer",
            ProviderType = "plex",
            Enabled = true,
            Status = MediaServerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.MediaServers.Add(mediaServer);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(mediaServer.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestServer"));
            Assert.That(result, Has.Property(nameof(result.ProviderType)).EqualTo("plex"));
            Assert.That(result, Has.Property(nameof(result.Enabled)).True);
            Assert.That(result, Has.Property(nameof(result.Status)).EqualTo(MediaServerStatus.Healthy));
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
    public async Task ListAllAsync_ReturnsAllMediaServers()
    {
        // Arrange
        var mediaServer1 = new MediaServer
        {
            Name = "Server1",
            ProviderType = "plex",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var mediaServer2 = new MediaServer
        {
            Name = "Server2",
            ProviderType = "jellyfin",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.MediaServers.AddRange(mediaServer1, mediaServer2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoMediaServers()
    {
        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesMediaServer()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Name = "NewServer",
            ProviderType = "emby",
            Enabled = true,
            Settings = new Dictionary<string, string> { ["host"] = "localhost" }
        };

        // Act
        var result = await _subject.CreateAsync(mediaServer, _userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewServer"));
            Assert.That(result, Has.Property(nameof(result.ProviderType)).EqualTo("emby"));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Name = "AuditedServer",
            ProviderType = "plex",
            Enabled = true
        };

        // Act
        await _subject.CreateAsync(mediaServer, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("MediaServer"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesMediaServer_WhenExists()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Name = "OriginalServer",
            ProviderType = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.MediaServers.Add(mediaServer);
        await _context.SaveChangesAsync();

        mediaServer.Name = "UpdatedServer";
        mediaServer.Enabled = false;
        mediaServer.Status = MediaServerStatus.Healthy;

        // Act
        var result = await _subject.UpdateAsync(mediaServer, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(mediaServer.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Name)).EqualTo("UpdatedServer"));
            Assert.That(updated, Has.Property(nameof(updated.Enabled)).False);
            Assert.That(updated, Has.Property(nameof(updated.Status)).EqualTo(MediaServerStatus.Healthy));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "NonExistent",
            ProviderType = "plex",
            Enabled = true
        };

        // Act
        var result = await _subject.UpdateAsync(mediaServer, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Name = "ToUpdate",
            ProviderType = "plex",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.MediaServers.Add(mediaServer);
        await _context.SaveChangesAsync();

        mediaServer.Name = "Updated";

        // Act
        await _subject.UpdateAsync(mediaServer, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("MediaServer"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesMediaServer_WhenExists()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Name = "ToDelete",
            ProviderType = "plex",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.MediaServers.Add(mediaServer);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(mediaServer.Id, _userId);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(mediaServer.Id);
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
        var mediaServer = new MediaServer
        {
            Name = "DeleteAudit",
            ProviderType = "plex",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.MediaServers.Add(mediaServer);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync(mediaServer.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("MediaServer"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesName_WhenNamePatchProvided()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Name = "OriginalName",
            ProviderType = "plex",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.MediaServers.Add(mediaServer);
        await _context.SaveChangesAsync();

        var patch = new MediaServerPatch(
            mediaServer.Id,
            Name: new ScalarPatch<string>("PatchedName"));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(mediaServer.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Has.Property(nameof(patched.Name)).EqualTo("PatchedName"));
            Assert.That(patched, Has.Property(nameof(patched.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(patched, Has.Property(nameof(patched.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesEnabled_WhenEnabledPatchProvided()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Name = "Server",
            ProviderType = "plex",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.MediaServers.Add(mediaServer);
        await _context.SaveChangesAsync();

        var patch = new MediaServerPatch(
            mediaServer.Id,
            Enabled: new ScalarPatch<bool>(false));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(mediaServer.Id);
        Assert.That(patched, Has.Property(nameof(patched.Enabled)).False);
    }

    [Test]
    public async Task PatchAsync_AddsSettings_WhenSettingsAddProvided()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Name = "Server",
            ProviderType = "plex",
            Enabled = true,
            Settings = new Dictionary<string, string> { ["existing"] = "value" },
            CreatedAt = DateTime.UtcNow
        };
        _context.MediaServers.Add(mediaServer);
        await _context.SaveChangesAsync();

        var patch = new MediaServerPatch(
            mediaServer.Id,
            Settings: new DictionaryPatch<string, string?, string?>(
                Add: new Dictionary<string, string?> { ["newkey"] = "newvalue" },
                Remove: null,
                Update: null));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(mediaServer.Id);
        Assert.That(patched, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Has.Property(nameof(patched.Settings)).ContainKey("existing"));
            Assert.That(patched, Has.Property(nameof(patched.Settings)).ContainKey("newkey").WithValue("newvalue"));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesSettings_WhenSettingsUpdateProvided()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Name = "Server",
            ProviderType = "plex",
            Enabled = true,
            Settings = new Dictionary<string, string> { ["host"] = "oldhost" },
            CreatedAt = DateTime.UtcNow
        };
        _context.MediaServers.Add(mediaServer);
        await _context.SaveChangesAsync();

        var patch = new MediaServerPatch(
            mediaServer.Id,
            Settings: new DictionaryPatch<string, string?, string?>(
                Add: null,
                Remove: null,
                Update: new Dictionary<string, string?> { ["host"] = "newhost" }));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(mediaServer.Id);
        Assert.That(patched, Is.Not.Null);
        Assert.That(patched, Has.Property(nameof(patched.Settings)).ContainKey("host").WithValue("newhost"));
    }

    [Test]
    public async Task PatchAsync_RemovesSettings_WhenSettingsRemoveProvided()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Name = "Server",
            ProviderType = "plex",
            Enabled = true,
            Settings = new Dictionary<string, string> { ["keep"] = "value", ["remove"] = "value" },
            CreatedAt = DateTime.UtcNow
        };
        _context.MediaServers.Add(mediaServer);
        await _context.SaveChangesAsync();

        var patch = new MediaServerPatch(
            mediaServer.Id,
            Settings: new DictionaryPatch<string, string?, string?>(
                Add: null,
                Remove: ["remove"],
                Update: null));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(mediaServer.Id);
        Assert.That(patched, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Has.Property(nameof(patched.Settings)).ContainKey("keep"));
            Assert.That(patched, Has.Property(nameof(patched.Settings)).Not.ContainKey("remove"));
        }
    }

    [Test]
    public async Task PatchAsync_AddsTags_WhenTagsAddProvided()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Name = "Server",
            ProviderType = "plex",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.MediaServers.Add(mediaServer);
        await _context.SaveChangesAsync();

        var patch = new MediaServerPatch(
            mediaServer.Id,
            Tags: new SetPatch<string, string>(Add: ["newtag"]));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var tag = await _context.Tags
            .Include(t => t.MediaServers)
            .FirstOrDefaultAsync(t => t.Name == "newtag");

        Assert.That(tag, Is.Not.Null);
        Assert.That(tag, Has.Property(nameof(tag.MediaServers))
            .With.One.With.Property(nameof(MediaServer.Id)).EqualTo(mediaServer.Id));
    }

    [Test]
    public async Task PatchAsync_RemovesTags_WhenTagsRemoveProvided()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Name = "Server",
            ProviderType = "plex",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var tag = new Tag { Name = "existingtag" };
        tag.MediaServers.Add(mediaServer);
        _context.MediaServers.Add(mediaServer);
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();

        var patch = new MediaServerPatch(
            mediaServer.Id,
            Tags: new SetPatch<string, string>(Remove: ["existingtag"]));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updatedTag = await _context.Tags
            .Include(t => t.MediaServers)
            .FirstOrDefaultAsync(t => t.Name == "existingtag");

        Assert.That(updatedTag, Is.Not.Null);
        Assert.That(updatedTag, Has.Property(nameof(updatedTag.MediaServers))
            .With.None.With.Property(nameof(MediaServer.Id)).EqualTo(mediaServer.Id));
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var patch = new MediaServerPatch(
            Guid.NewGuid(),
            Name: new ScalarPatch<string>("PatchedName"));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Name = "ToPatch",
            ProviderType = "plex",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.MediaServers.Add(mediaServer);
        await _context.SaveChangesAsync();

        var patch = new MediaServerPatch(
            mediaServer.Id,
            Name: new ScalarPatch<string>("Patched"));

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("MediaServer"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesTagAuditRecords_WhenTagsModified()
    {
        // Arrange
        var mediaServer = new MediaServer
        {
            Name = "TagAuditServer",
            ProviderType = "plex",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var existingTag = new Tag { Name = "removeTag" };
        existingTag.MediaServers.Add(mediaServer);
        _context.MediaServers.Add(mediaServer);
        _context.Tags.Add(existingTag);
        await _context.SaveChangesAsync();

        var patch = new MediaServerPatch(
            mediaServer.Id,
            Tags: new SetPatch<string, string>(
                Add: ["addTag"],
                Remove: ["removeTag"]));

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(3));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("AddTag"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("RemoveTag"));
        }
    }
}
