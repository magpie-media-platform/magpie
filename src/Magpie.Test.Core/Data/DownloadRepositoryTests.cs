using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models.Downloads;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using DownloadClientEntity = Magpie.Data.Database.Entities.DownloadClient;
using DownloadEntity = Magpie.Data.Database.Entities.Download;
using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class DownloadRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IDownloadRepository _subject = null!;
    private Guid _userId;
    private Guid _downloadClientId;

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

        var downloadClient = new DownloadClientEntity
        {
            Name = "TestClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Users.Add(user);
        _context.DownloadClients.Add(downloadClient);
        _context.SaveChanges();

        _userId = user.Id;
        _downloadClientId = downloadClient.Id;

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var historyRepo = new HistoryRepository(_context, loggerFactory.CreateLogger<HistoryRepository>());

        _subject = new DownloadRepository(_context, historyRepo, loggerFactory.CreateLogger<DownloadRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsDownload()
    {
        // Arrange
        var entity = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-123",
            Title = "TestDownload",
            State = DownloadState.Grabbed,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Downloads.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Download?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Download.Title))
                .EqualTo("TestDownload")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Download.ExternalId))
                .EqualTo("ext-123")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Download.State))
                .EqualTo(DownloadState.Grabbed));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Download?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllDownloads()
    {
        // Arrange
        var entity1 = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-1",
            Title = "Download1",
            State = DownloadState.Grabbed,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-2",
            Title = "Download2",
            State = DownloadState.Downloading,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Downloads.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Download>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoDownloads()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Download>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByContentItemIdAsync_ReturnsDownloads()
    {
        // Arrange - Downloads without content item are used; test verifies filtering works
        var entity1 = new DownloadEntity
        {
            ContentItemId = null,
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-1",
            Title = "Download1",
            State = DownloadState.Grabbed,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new DownloadEntity
        {
            ContentItemId = null,
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-2",
            Title = "Download2",
            State = DownloadState.Downloading,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Downloads.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act - Query for a non-existent content item
        var result = await _subject.GetByContentItemIdAsync(Guid.NewGuid());

        // Assert - Should return empty list since none match
        var success = result as SuccessResult<IReadOnlyList<Download>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Is.Empty);
    }

    [Test]
    public async Task GetByContentItemIdAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByContentItemIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Download>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByDownloadClientIdAsync_ReturnsDownloads()
    {
        // Arrange
        var otherClient = new DownloadClientEntity
        {
            Name = "OtherClient",
            Provider = "sabnzbd",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(otherClient);
        _context.SaveChanges();

        var entity1 = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-1",
            Title = "Download1",
            State = DownloadState.Grabbed,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-2",
            Title = "Download2",
            State = DownloadState.Downloading,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new DownloadEntity
        {
            DownloadClientId = otherClient.Id,
            ExternalId = "ext-3",
            Title = "Download3",
            State = DownloadState.Grabbed,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Downloads.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByDownloadClientIdAsync(_downloadClientId);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Download>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByDownloadClientIdAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByDownloadClientIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Download>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByExternalIdAsync_ReturnsDownload()
    {
        // Arrange
        var entity = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "unique-ext-id",
            Title = "TestDownload",
            State = DownloadState.Grabbed,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Downloads.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByExternalIdAsync(_downloadClientId, "unique-ext-id");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Download?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Download.Title))
                .EqualTo("TestDownload")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Download.ExternalId))
                .EqualTo("unique-ext-id"));
    }

    [Test]
    public async Task GetByExternalIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByExternalIdAsync(_downloadClientId, "nonexistent");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Download?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByStateAsync_ReturnsDownloads()
    {
        // Arrange
        var entity1 = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-1",
            Title = "Download1",
            State = DownloadState.Downloading,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-2",
            Title = "Download2",
            State = DownloadState.Downloading,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-3",
            Title = "Download3",
            State = DownloadState.Downloaded,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Downloads.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByStateAsync(DownloadState.Downloading);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Download>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(Download.State)).EqualTo(DownloadState.Downloading));
        }
    }

    [Test]
    public async Task GetByStateAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        var entity = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-1",
            Title = "Download1",
            State = DownloadState.Grabbed,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Downloads.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByStateAsync(DownloadState.Failed);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Download>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesDownload()
    {
        // Arrange
        var model = new Download(
            Guid.Empty,
            null,
            null,
            _downloadClientId,
            "new-ext-id",
            "NewDownload",
            DownloadState.Grabbed,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from d in _context.Downloads.AsNoTracking()
            where d.Title == "NewDownload"
            select d).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Download>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Download.Title))
                    .EqualTo("NewDownload")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Download.ExternalId))
                    .EqualTo("new-ext-id")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Download.State))
                    .EqualTo(DownloadState.Grabbed));
            Assert.That(entity, Is.InstanceOf<DownloadEntity>()
                .With.Property(nameof(DownloadEntity.Title)).EqualTo("NewDownload")
                .And.Property(nameof(DownloadEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Download")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesDownloadWithAllFields()
    {
        // Arrange
        var dateCompleted = DateTimeOffset.UtcNow;

        var model = new Download(
            Guid.Empty,
            null,
            null,
            _downloadClientId,
            "complete-ext-id",
            "CompleteDownload",
            DownloadState.Downloaded,
            "/output/path",
            "Some failure reason",
            dateCompleted,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from d in _context.Downloads.AsNoTracking()
            where d.Title == "CompleteDownload"
            select d).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Download>>());
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.OutputPath, Is.EqualTo("/output/path"));
            Assert.That(entity.FailureReason, Is.EqualTo("Some failure reason"));
            Assert.That(entity.DateCompleted, Is.EqualTo(dateCompleted));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesDownload()
    {
        // Arrange
        var entity = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "original-ext",
            Title = "OriginalTitle",
            State = DownloadState.Grabbed,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Downloads.Add(entity);
        _context.SaveChanges();

        var model = new Download(
            entity.Id,
            null,
            null,
            _downloadClientId,
            "updated-ext",
            "UpdatedTitle",
            DownloadState.Downloading,
            "/new/path",
            null,
            null,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from d in _context.Downloads.AsNoTracking()
            where d.Id == entity.Id
            select d).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<DownloadEntity>()
                .With.Property(nameof(DownloadEntity.Title)).EqualTo("UpdatedTitle")
                .And.Property(nameof(DownloadEntity.ExternalId)).EqualTo("updated-ext")
                .And.Property(nameof(DownloadEntity.State)).EqualTo(DownloadState.Downloading)
                .And.Property(nameof(DownloadEntity.OutputPath)).EqualTo("/new/path")
                .And.Property(nameof(DownloadEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(DownloadEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Download")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new Download(
            Guid.NewGuid(),
            null,
            null,
            _downloadClientId,
            "ext-id",
            "NonExistent",
            DownloadState.Grabbed,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateStateAsync_UpdatesStateAndRelatedFields()
    {
        // Arrange
        var entity = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-123",
            Title = "TestDownload",
            State = DownloadState.Downloading,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Downloads.Add(entity);
        _context.SaveChanges();

        var dateCompleted = DateTimeOffset.UtcNow;

        // Act
        var result = await _subject.UpdateStateAsync(
            entity.Id,
            DownloadState.Downloaded,
            "/completed/path",
            null,
            dateCompleted,
            _userId);

        // Assert
        var updatedEntity = await _context.Downloads
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<DownloadEntity>()
                .With.Property(nameof(DownloadEntity.State)).EqualTo(DownloadState.Downloaded)
                .And.Property(nameof(DownloadEntity.OutputPath)).EqualTo("/completed/path")
                .And.Property(nameof(DownloadEntity.DateCompleted)).EqualTo(dateCompleted)
                .And.Property(nameof(DownloadEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(DownloadEntity.ModifiedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateStateAsync_UpdatesFailureReason()
    {
        // Arrange
        var entity = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-123",
            Title = "TestDownload",
            State = DownloadState.Downloading,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Downloads.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.UpdateStateAsync(
            entity.Id,
            DownloadState.Failed,
            null,
            "Connection timeout",
            null,
            _userId);

        // Assert
        var updatedEntity = await _context.Downloads
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<DownloadEntity>()
                .With.Property(nameof(DownloadEntity.State)).EqualTo(DownloadState.Failed)
                .And.Property(nameof(DownloadEntity.FailureReason)).EqualTo("Connection timeout"));
        }
    }

    [Test]
    public async Task UpdateStateAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.UpdateStateAsync(
            Guid.NewGuid(),
            DownloadState.Downloaded,
            null,
            null,
            null,
            _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateStateAsync_DoesNotCreateHistoryEntry()
    {
        // Arrange
        var entity = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-123",
            Title = "TestDownload",
            State = DownloadState.Downloading,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Downloads.Add(entity);
        _context.SaveChanges();

        // Act
        await _subject.UpdateStateAsync(
            entity.Id,
            DownloadState.Downloaded,
            "/path",
            null,
            DateTimeOffset.UtcNow,
            _userId);

        // Assert
        var historyEntries = await _context.HistoryEntries.ToListAsync();
        Assert.That(historyEntries, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_DeletesDownload()
    {
        // Arrange
        var entity = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-to-delete",
            Title = "ToDelete",
            State = DownloadState.Grabbed,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Downloads.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.Downloads.AnyAsync(d => d.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Download")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task DeleteByDownloadClientIdAsync_DeletesDownloadsForClient()
    {
        // Arrange
        var otherClient = new DownloadClientEntity
        {
            Name = "OtherClient",
            Provider = "sabnzbd",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(otherClient);
        _context.SaveChanges();

        var entity1 = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-1",
            Title = "Download1",
            State = DownloadState.Grabbed,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new DownloadEntity
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-2",
            Title = "Download2",
            State = DownloadState.Downloading,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new DownloadEntity
        {
            DownloadClientId = otherClient.Id,
            ExternalId = "ext-3",
            Title = "Download3",
            State = DownloadState.Grabbed,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Downloads.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteByDownloadClientIdAsync(_downloadClientId, _userId);

        // Assert
        var clientDownloadsExist = await _context.Downloads.AnyAsync(d => d.DownloadClientId == _downloadClientId);
        var otherDownloadExists = await _context.Downloads.AnyAsync(d => d.Title == "Download3");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<int>>()
                .With.Property(nameof(SuccessResult<>.Value)).EqualTo(2));
            Assert.That(clientDownloadsExist, Is.False);
            Assert.That(otherDownloadExists, Is.True);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Download"));
        }
    }

    [Test]
    public async Task DeleteByDownloadClientIdAsync_ReturnsZero_WhenNoMatches()
    {
        // Act
        var result = await _subject.DeleteByDownloadClientIdAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<int>>()
            .With.Property(nameof(SuccessResult<>.Value)).EqualTo(0));
    }
}
