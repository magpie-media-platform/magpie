using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

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

        var downloadClient = new DownloadClient
        {
            Name = "TestClient",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.DownloadClients.Add(downloadClient);
        _context.SaveChanges();

        _userId = user.Id;
        _downloadClientId = downloadClient.Id;

        var loggerFactory = LoggerFactory.Create(builder => builder.AddTestLogger());

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());

        var transactionManager = new TransactionManager(_context);

        _subject = new DownloadRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<DownloadRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsDownload_WhenExists()
    {
        // Arrange
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-123",
            Title = "TestDownload",
            Status = DownloadStatus.Grabbed,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(download.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("TestDownload"));
            Assert.That(result, Has.Property(nameof(result.ExternalId)).EqualTo("ext-123"));
            Assert.That(result, Has.Property(nameof(result.Status)).EqualTo(DownloadStatus.Grabbed));
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
    public async Task ListAllAsync_ReturnsAllDownloads()
    {
        // Arrange
        var download1 = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-1",
            Title = "Download1",
            Status = DownloadStatus.Grabbed,
            CreatedAt = DateTime.UtcNow
        };
        var download2 = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-2",
            Title = "Download2",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.AddRange(download1, download2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoDownloads()
    {
        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task QueryAsync_FiltersByDownloadClientId()
    {
        // Arrange
        var otherClient = new DownloadClient
        {
            Name = "OtherClient",
            ProviderType = "sabnzbd",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(otherClient);
        await _context.SaveChangesAsync();

        var download1 = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-1",
            Title = "Download1",
            Status = DownloadStatus.Grabbed,
            CreatedAt = DateTime.UtcNow
        };
        var download2 = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-2",
            Title = "Download2",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        var download3 = new Download
        {
            DownloadClientId = otherClient.Id,
            ExternalId = "ext-3",
            Title = "Download3",
            Status = DownloadStatus.Grabbed,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.AddRange(download1, download2, download3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new() { DownloadClientId = _downloadClientId }, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByStatus()
    {
        // Arrange
        var download1 = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-1",
            Title = "Download1",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        var download2 = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-2",
            Title = "Download2",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        var download3 = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-3",
            Title = "Download3",
            Status = DownloadStatus.Downloaded,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.AddRange(download1, download2, download3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new() { Status = DownloadStatus.Downloading }, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(Download.Status)).EqualTo(DownloadStatus.Downloading));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-1",
            Title = "Download1",
            Status = DownloadStatus.Grabbed,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new() { Status = DownloadStatus.Failed }, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Empty);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(0));
        }
    }

    [Test]
    public async Task QueryAsync_AppliesPaging()
    {
        // Arrange
        for (var i = 0; i < 10; i++)
        {
            _context.Downloads.Add(new()
            {
                DownloadClientId = _downloadClientId,
                ExternalId = $"ext-{i}",
                Title = $"Download{i}",
                Status = DownloadStatus.Grabbed,
                CreatedAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new(), new() { PageNumber = 2, PageSize = 3 }, null);

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
    public async Task CreateAsync_CreatesDownload()
    {
        // Arrange
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "new-ext-id",
            Title = "NewDownload",
            Status = DownloadStatus.Grabbed
        };

        // Act
        var result = await _subject.CreateAsync(download, _userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("NewDownload"));
            Assert.That(result, Has.Property(nameof(result.ExternalId)).EqualTo("new-ext-id"));
            Assert.That(result, Has.Property(nameof(result.Status)).EqualTo(DownloadStatus.Grabbed));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesDownloadWithAllFields()
    {
        // Arrange
        var dateCompleted = DateTime.UtcNow;

        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "complete-ext-id",
            Title = "CompleteDownload",
            Status = DownloadStatus.Downloaded,
            OutputPath = "/output/path",
            StatusReason = "Some reason",
            DateCompleted = dateCompleted
        };

        // Act
        var result = await _subject.CreateAsync(download, _userId);

        // Assert
        var persisted = await _context.Downloads
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == result.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted, Has.Property(nameof(persisted.OutputPath)).EqualTo("/output/path"));
            Assert.That(persisted, Has.Property(nameof(persisted.StatusReason)).EqualTo("Some reason"));
            Assert.That(persisted, Has.Property(nameof(persisted.DateCompleted)).EqualTo(dateCompleted));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord()
    {
        // Arrange
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "audit-ext-id",
            Title = "AuditDownload",
            Status = DownloadStatus.Grabbed
        };

        // Act
        await _subject.CreateAsync(download, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Download"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesDownload_WhenExists()
    {
        // Arrange
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "original-ext",
            Title = "OriginalTitle",
            Status = DownloadStatus.Grabbed,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync();

        download.Title = "UpdatedTitle";
        download.ExternalId = "updated-ext";
        download.Status = DownloadStatus.Downloading;
        download.OutputPath = "/new/path";

        // Act
        var result = await _subject.UpdateAsync(download, _userId);

        // Assert
        var updated = await _subject.GetByIdAsync(download.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated, Has.Property(nameof(updated.Title)).EqualTo("UpdatedTitle"));
            Assert.That(updated, Has.Property(nameof(updated.ExternalId)).EqualTo("updated-ext"));
            Assert.That(updated, Has.Property(nameof(updated.Status)).EqualTo(DownloadStatus.Downloading));
            Assert.That(updated, Has.Property(nameof(updated.OutputPath)).EqualTo("/new/path"));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var download = new Download
        {
            Id = Guid.NewGuid(),
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-id",
            Title = "NonExistent",
            Status = DownloadStatus.Grabbed
        };

        // Act
        var result = await _subject.UpdateAsync(download, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord()
    {
        // Arrange
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "update-ext",
            Title = "ToUpdate",
            Status = DownloadStatus.Grabbed,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync();

        download.Title = "Updated";

        // Act
        await _subject.UpdateAsync(download, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Download"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesDownload_WhenExists()
    {
        // Arrange
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-to-delete",
            Title = "ToDelete",
            Status = DownloadStatus.Grabbed,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(download.Id, _userId);

        // Assert
        var deleted = await _subject.GetByIdAsync(download.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(deleted, Is.Null);
        }
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
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "delete-ext",
            Title = "ToDelete",
            Status = DownloadStatus.Grabbed,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync(download.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Download"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

}
