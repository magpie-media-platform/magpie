using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
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
public sealed class DownloadRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IDownloadRepository _subject = null!;
    private ITransactionManager _transactionManager = null!;
    private List<MagpieEvent> _publishedEvents = null!;
    private Mock<IDataProtectionService> _protector = null!;
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
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.DownloadClients.Add(downloadClient);
        _context.SaveChanges();

        _userId = user.Id;
        _downloadClientId = downloadClient.Id;

        var loggerFactory = NullLoggerFactory.Instance;

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));

        _transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _publishedEvents = [];
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) => _publishedEvents.Add(magpieEvent))
            .Returns(Task.CompletedTask);

        _protector = new Mock<IDataProtectionService>();
        _protector.Setup(p => p.Unprotect(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns((IReadOnlyDictionary<string, string> settings) => new Dictionary<string, string>(settings));

        _subject = new DownloadRepository(
            _context,
            auditRecordRepository,
            _transactionManager,
            eventBus.Object,
            loggerFactory.CreateLogger<DownloadRepository>(),
            _protector.Object,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task UpdateRefreshedStatusAsync_DefersStatusEvent_WhenTheOuterTransactionIsOpen()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "wrapped-refresh",
            Title = "Wrapped",
            Status = DownloadStatus.Downloading,
            CreatedAt = now
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _publishedEvents.Clear();
        var eventsDuringTransaction = -1;

        // Act
        await _transactionManager.ExecuteWithTransactionAsync(async ct =>
        {
            await _subject.UpdateRefreshedStatusAsync(
                download.Id,
                DownloadStatus.Downloaded,
                "client says done",
                outputPath: null,
                dateCompleted: now,
                progress: 1.0m,
                lastProgressAt: now,
                heldByPolicy: false,
                failureReason: null,
                failedAt: null,
                now,
                _userId,
                ct).ConfigureAwait(false);
            eventsDuringTransaction = _publishedEvents.Count;
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(eventsDuringTransaction, Is.Zero);
            Assert.That(_publishedEvents, Has.Count.EqualTo(1));
            Assert.That(_publishedEvents[0].Name, Is.EqualTo("download.completed"));
        }
    }

    [Test]
    public void UpdateRefreshedStatusAsync_DropsStatusEvent_WhenWrappedTransactionRollsBack()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "wrapped-rollback",
            Title = "WrappedRollback",
            Status = DownloadStatus.Downloading,
            CreatedAt = now
        };
        _context.Downloads.Add(download);
        _context.SaveChanges();
        _publishedEvents.Clear();

        // Act
        var act = (AsyncTestDelegate)(async () =>
            await _transactionManager.ExecuteWithTransactionAsync(async ct =>
            {
                await _subject.UpdateRefreshedStatusAsync(
                    download.Id,
                    DownloadStatus.Downloaded,
                    "client says done",
                    outputPath: null,
                    dateCompleted: now,
                    progress: 1.0m,
                    lastProgressAt: now,
                    heldByPolicy: false,
                    failureReason: null,
                    failedAt: null,
                    now,
                    _userId,
                    ct).ConfigureAwait(false);
                throw new InvalidOperationException("boom");
            }).ConfigureAwait(false));

        // Assert
        Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.That(_publishedEvents, Is.Empty);
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(download.Id).ConfigureAwait(false);

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
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllDownloads_WhenDownloadsExist()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoDownloads()
    {
        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task QueryAsync_FiltersByDownloadClientId_WhenClientIdFilterIsSet()
    {
        // Arrange
        var otherClient = new DownloadClient
        {
            Name = "OtherClient",
            ProviderType = "sabnzbd",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(otherClient);
        await _context.SaveChangesAsync().ConfigureAwait(false);

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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { DownloadClientId = _downloadClientId },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByStatus_WhenStatusFilterIsSet()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { Status = DownloadStatus.Downloading },
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { Status = DownloadStatus.Failed },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Empty);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(0));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByContentItemId_WhenOnlyCandidateScopeExists()
    {
        // Arrange
        var metadataContainerId = Guid.NewGuid();
        var hierarchyItemId = Guid.NewGuid();
        var metadataContainer = new MetadataContainer
        {
            Id = metadataContainerId,
            CreatedAt = DateTime.UtcNow
        };
        var hierarchyItem = new ContentHierarchyItem
        {
            Id = hierarchyItemId,
            MetadataContainerId = metadataContainerId,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-1",
            Name = "Episode 1",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var release = new Release
        {
            Protocol = "torrent",
            Title = "Show S01E01",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var download = new Download
        {
            Release = release,
            DownloadClientId = _downloadClientId,
            ExternalId = "download-1",
            Title = "Show S01E01",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        var releaseScope = new ReleaseScope
        {
            Release = release,
            ContentHierarchyItem = hierarchyItem,
        };
        _context.MetadataContainers.Add(metadataContainer);
        _context.ContentHierarchyItems.Add(hierarchyItem);
        _context.Releases.Add(release);
        _context.Downloads.Add(download);
        _context.ReleaseScopes.Add(releaseScope);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { ContentItemId = hierarchyItemId },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Empty);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(0));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByContentItemId_WhenRealizedScopeExists()
    {
        // Arrange
        var metadataContainerId = Guid.NewGuid();
        var hierarchyItemId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        var downloadId = Guid.NewGuid();
        var metadataContainer = new MetadataContainer
        {
            Id = metadataContainerId,
            CreatedAt = DateTime.UtcNow
        };
        var hierarchyItem = new ContentHierarchyItem
        {
            Id = hierarchyItemId,
            MetadataContainerId = metadataContainerId,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-1",
            Name = "Episode 1",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var release = new Release
        {
            Id = releaseId,
            Protocol = "torrent",
            Title = "Show S01E01",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var download = new Download
        {
            Id = downloadId,
            ReleaseId = releaseId,
            DownloadClientId = _downloadClientId,
            ExternalId = "download-1",
            Title = "Show S01E01",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(metadataContainer);
        _context.ContentHierarchyItems.Add(hierarchyItem);
        _context.Releases.Add(release);
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.DownloadScopes.Add(new DownloadScope
        {
            DownloadId = downloadId,
            ContentHierarchyItemId = hierarchyItemId,
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { ContentItemId = hierarchyItemId },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(1).Items);
            Assert.That(result.Items.Single(), Has.Property(nameof(Download.Id)).EqualTo(downloadId));
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(1));
        }
    }

    [Test]
    public async Task QueryAsync_UnionsScalarAndCollectionIdsWithinEachFilterFamily_WhenBothFilterFormsAreSet()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var metadataContainerA = new MetadataContainer { CreatedAt = now };
        var metadataContainerB = new MetadataContainer { CreatedAt = now };
        var metadataContainerC = new MetadataContainer { CreatedAt = now };
        var hierarchyItemA = new ContentHierarchyItem
        {
            MetadataContainer = metadataContainerA,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-a",
            Name = "Episode A",
            Monitoring = true,
            CreatedAt = now
        };
        var hierarchyItemB = new ContentHierarchyItem
        {
            MetadataContainer = metadataContainerB,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-b",
            Name = "Episode B",
            Monitoring = true,
            CreatedAt = now
        };
        var hierarchyItemC = new ContentHierarchyItem
        {
            MetadataContainer = metadataContainerC,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-c",
            Name = "Episode C",
            Monitoring = true,
            CreatedAt = now
        };
        var releaseA = new Release
        {
            Protocol = "torrent",
            Title = "Release A",
            PublishDate = now,
            CreatedAt = now
        };
        var releaseB = new Release
        {
            Protocol = "torrent",
            Title = "Release B",
            PublishDate = now,
            CreatedAt = now
        };
        var releaseC = new Release
        {
            Protocol = "torrent",
            Title = "Release C",
            PublishDate = now,
            CreatedAt = now
        };
        var downloadA = new Download
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Release = releaseA,
            DownloadClientId = _downloadClientId,
            ExternalId = "download-a",
            Title = "Download A",
            Status = DownloadStatus.Downloading,
            CreatedAt = now
        };
        var downloadB = new Download
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Release = releaseB,
            DownloadClientId = _downloadClientId,
            ExternalId = "download-b",
            Title = "Download B",
            Status = DownloadStatus.Downloading,
            CreatedAt = now
        };
        var wrongRelease = new Download
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
            Release = releaseC,
            DownloadClientId = _downloadClientId,
            ExternalId = "wrong-release",
            Title = "Wrong release",
            Status = DownloadStatus.Downloading,
            CreatedAt = now
        };
        var wrongScope = new Download
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
            Release = releaseA,
            DownloadClientId = _downloadClientId,
            ExternalId = "wrong-scope",
            Title = "Wrong scope",
            Status = DownloadStatus.Downloading,
            CreatedAt = now
        };
        _context.ContentHierarchyItems.AddRange(hierarchyItemA, hierarchyItemB, hierarchyItemC);
        _context.Downloads.AddRange(downloadA, downloadB, wrongRelease, wrongScope);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.AddRange(
            new DownloadScope
            {
                DownloadId = downloadA.Id,
                ContentHierarchyItemId = hierarchyItemB.Id
            },
            new DownloadScope
            {
                DownloadId = downloadB.Id,
                ContentHierarchyItemId = hierarchyItemA.Id
            },
            new DownloadScope
            {
                DownloadId = wrongRelease.Id,
                ContentHierarchyItemId = hierarchyItemA.Id
            },
            new DownloadScope
            {
                DownloadId = wrongScope.Id,
                ContentHierarchyItemId = hierarchyItemC.Id
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new()
            {
                ReleaseId = releaseA.Id,
                ReleaseIds = [releaseB.Id],
                ContentItemId = hierarchyItemA.Id,
                ContentHierarchyItemIds = [hierarchyItemB.Id]
            },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items.Select(download => download.Id), Is.EqualTo([downloadA.Id, downloadB.Id]));
            Assert.That(result.TotalItems, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_AppliesPaging_WhenPagingIsSet()
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
    public async Task CreateAsync_CreatesDownload_WhenRequestIsValid()
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
        var result = await _subject.CreateAsync(download, _userId).ConfigureAwait(false);

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
    public async Task CreateAsync_CreatesDownloadWithAllFields_WhenAllFieldsAreSet()
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
        var result = await _subject.CreateAsync(download, _userId).ConfigureAwait(false);

        // Assert
        var persisted = await _context.Downloads
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == result.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted, Has.Property(nameof(persisted.OutputPath)).EqualTo("/output/path"));
            Assert.That(persisted, Has.Property(nameof(persisted.StatusReason)).EqualTo("Some reason"));
            Assert.That(persisted, Has.Property(nameof(persisted.DateCompleted)).EqualTo(dateCompleted));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord_WhenDownloadIsCreated()
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
        await _subject.CreateAsync(download, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        download.Title = "UpdatedTitle";
        download.ExternalId = "updated-ext";
        download.Status = DownloadStatus.Downloading;
        download.OutputPath = "/new/path";

        // Act
        var result = await _subject.UpdateAsync(download, _userId).ConfigureAwait(false);

        // Assert
        var updated = await _subject.GetByIdAsync(download.Id).ConfigureAwait(false);
        var downloadEvent = _publishedEvents.Single(e => e.Name == "download.updated");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated, Has.Property(nameof(updated.Title)).EqualTo("UpdatedTitle"));
            Assert.That(updated, Has.Property(nameof(updated.ExternalId)).EqualTo("updated-ext"));
            Assert.That(updated, Has.Property(nameof(updated.Status)).EqualTo(DownloadStatus.Downloading));
            Assert.That(updated, Has.Property(nameof(updated.OutputPath)).EqualTo("/new/path"));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
            Assert.That(downloadEvent.Context["download.id"], Is.EqualTo(download.Id.ToString("D")));
            Assert.That(downloadEvent.Context["download.changedFields"], Does.Contain("title"));
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
        var result = await _subject.UpdateAsync(download, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord_WhenDownloadIsUpdated()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        download.Title = "Updated";

        // Act
        await _subject.UpdateAsync(download, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(download.Id, _userId).ConfigureAwait(false);

        // Assert
        var deleted = await _subject.GetByIdAsync(download.Id).ConfigureAwait(false);
        var downloadEvent = _publishedEvents.Single(e => e.Name == "download.deleted");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(deleted, Is.Null);
            Assert.That(downloadEvent.Context["download.id"], Is.EqualTo(download.Id.ToString("D")));
        }
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
    public async Task DeleteAsync_CreatesAuditRecord_WhenDownloadIsDeleted()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(download.Id, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Download"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task RetryAsync_TransitionsRowClearsMetadataAndAudits_WhenDownloadIsFailed()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "test",
            Level = "level",
            Slug = Guid.NewGuid().ToString("N"),
            Name = $"Item-{Guid.NewGuid():N}",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(hierarchyItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contentItemId = contentItem.Id;
        var release = new Release
        {
            Protocol = "torrent",
            Title = $"Release-{Guid.NewGuid():N}",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            InfoUrl = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var nodeId = await _context.ContentItems
            .Where(c => c.Id == contentItemId)
            .Select(c => c.ContentHierarchyItemId)
            .FirstAsync().ConfigureAwait(false);
        _context.ReleaseScopes.Add(new ReleaseScope { ReleaseId = release.Id, ContentHierarchyItemId = nodeId, });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var releaseId = release.Id;
        var download = new Download
        {
            ReleaseId = releaseId,
            DownloadClientId = _downloadClientId,
            ExternalId = "retry-ext",
            Title = "FailedItem",
            Status = DownloadStatus.Failed,
            FailureReason = DownloadFailureReason.ImportFailed,
            FailedAt = DateTime.UtcNow,
            StatusReason = "Import failed",
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var releaseScope = await _context.ReleaseScopes.FirstAsync(c => c.ReleaseId == releaseId).ConfigureAwait(false);
        _context.DownloadScopes.Add(new DownloadScope
        {
            DownloadId = download.Id,
            ContentHierarchyItemId = releaseScope.ContentHierarchyItemId
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.RetryAsync(download.Id, _userId).ConfigureAwait(false);

        // Assert
        var updated = await _context.Downloads.AsNoTracking().FirstAsync(d => d.Id == download.Id)
            .ConfigureAwait(false);
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(DownloadRetryOutcome.Retried));
            Assert.That(result.ContentItemId, Is.EqualTo(contentItemId));

            Assert.That(updated.Status, Is.EqualTo(DownloadStatus.Cancelled));
            Assert.That(updated.StatusReason, Is.EqualTo("Operator retry"));
            Assert.That(updated.FailureReason, Is.Null);
            Assert.That(updated.FailedAt, Is.Null);
            Assert.That(updated.ModifiedByUserId, Is.EqualTo(_userId));
            Assert.That(updated.ModifiedAt, Is.Not.Null);

            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Retry"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Download"));
            Assert.That(
                auditRecords,
                Has.One.With.Property(nameof(AuditRecord.EntityId))
                    .EqualTo(download.Id.ToString()));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
            Assert.That(auditRecords[0].Detail, Does.Contain("Operator retry"));
        }
    }

    [Test]
    public async Task RetryAsync_ReturnsNullContentItemId_WhenDownloadHasNoRelease()
    {
        // Arrange
        var download = new Download
        {
            ReleaseId = null,
            DownloadClientId = _downloadClientId,
            ExternalId = "retry-orphan-ext",
            Title = "OrphanFailed",
            Status = DownloadStatus.Failed,
            FailureReason = DownloadFailureReason.ImportFailed,
            FailedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.RetryAsync(download.Id, _userId).ConfigureAwait(false);

        // Assert
        var updated = await _context.Downloads.AsNoTracking().FirstAsync(d => d.Id == download.Id)
            .ConfigureAwait(false);
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(DownloadRetryOutcome.Retried));
            Assert.That(result.ContentItemId, Is.Null);
            Assert.That(updated.Status, Is.EqualTo(DownloadStatus.Cancelled));
            Assert.That(auditRecords, Has.Count.EqualTo(1));
        }
    }

    [TestCase(DownloadStatus.Queued)]
    [TestCase(DownloadStatus.Downloading)]
    [TestCase(DownloadStatus.Imported)]
    [TestCase(DownloadStatus.Cancelled)]
    public async Task RetryAsync_ReturnsConflictAndWritesNothing_WhenDownloadIsNotFailed(DownloadStatus startStatus)
    {
        // Arrange
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "retry-conflict-ext",
            Title = "NotFailed",
            Status = startStatus,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.RetryAsync(download.Id, _userId).ConfigureAwait(false);

        // Assert
        var unchanged = await _context.Downloads.AsNoTracking().FirstAsync(d => d.Id == download.Id)
            .ConfigureAwait(false);
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(DownloadRetryOutcome.Conflict));
            Assert.That(result.ContentItemId, Is.Null);
            Assert.That(unchanged.Status, Is.EqualTo(startStatus));
            Assert.That(auditRecords, Is.Empty);
        }
    }

    [Test]
    public async Task RetryAsync_ReturnsNotFoundAndWritesNothing_WhenDownloadDoesNotExist()
    {
        // Act
        var result = await _subject.RetryAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(DownloadRetryOutcome.NotFound));
            Assert.That(result.ContentItemId, Is.Null);
            Assert.That(auditRecords, Is.Empty);
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingStatuses_WhenAStatusSetIsProvided()
    {
        // Arrange
        _context.Downloads.AddRange(
            new Download
            {
                DownloadClientId = _downloadClientId,
                ExternalId = "ext-q",
                Title = "Queued",
                Status = DownloadStatus.Queued,
                CreatedAt = DateTime.UtcNow
            },
            new Download
            {
                DownloadClientId = _downloadClientId,
                ExternalId = "ext-d",
                Title = "Downloading",
                Status = DownloadStatus.Downloading,
                CreatedAt = DateTime.UtcNow
            },
            new Download
            {
                DownloadClientId = _downloadClientId,
                ExternalId = "ext-i",
                Title = "Imported",
                Status = DownloadStatus.Imported,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new() { Statuses = [DownloadStatus.Queued, DownloadStatus.Downloading] },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.None.Property(nameof(Download.Status)).EqualTo(DownloadStatus.Imported));
        }
    }

    [Test]
    public async Task QueryAsync_SortsByDateCompletedDescending_WhenCompletionDatesDiffer()
    {
        // Arrange
        var completed = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-completed",
            Title = "Completed",
            Status = DownloadStatus.Imported,
            DateCompleted = new DateTime(2024, 1, 14, 3, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2024, 1, 8, 3, 0, 0, DateTimeKind.Utc)
        };
        var cancelled = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-cancelled",
            Title = "Cancelled",
            Status = DownloadStatus.Cancelled,
            DateCompleted = new DateTime(2024, 1, 11, 3, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2024, 1, 9, 3, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2024, 1, 15, 3, 0, 0, DateTimeKind.Utc)
        };
        var failed = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-failed",
            Title = "Failed",
            Status = DownloadStatus.Failed,
            DateCompleted = new DateTime(2024, 1, 12, 3, 0, 0, DateTimeKind.Utc),
            FailedAt = new DateTime(2024, 1, 16, 3, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2024, 1, 10, 3, 0, 0, DateTimeKind.Utc)
        };
        _context.Downloads.AddRange(completed, cancelled, failed);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "dateCompleted", Direction = SortDirection.Descending };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items.Select(i => i.Title), Is.EqualTo(["Failed", "Cancelled", "Completed"]));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsLatestScopedImport_WhenHistoryIncludesUnrelatedAndIncompleteDownloads()
    {
        // Arrange
        var node = new ContentHierarchyItem
        {
            ContentType = "type-a",
            Level = "leaf",
            Slug = "example",
            Name = "Example",
            MetadataContainer = new MetadataContainer()
        };
        var older = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "older",
            Title = "Older",
            Status = DownloadStatus.Imported,
            DateCompleted = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var latest = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "latest",
            Title = "Latest",
            Status = DownloadStatus.Imported,
            ModifiedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };
        var unrelated = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "unrelated",
            Title = "Unrelated",
            Status = DownloadStatus.Imported,
            DateCompleted = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)
        };
        var incomplete = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "incomplete",
            Title = "Incomplete",
            Status = DownloadStatus.Downloading,
            CreatedAt = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc)
        };
        _context.ContentHierarchyItems.Add(node);
        _context.Downloads.AddRange(older, latest, unrelated, incomplete);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.AddRange(
            new DownloadScope { DownloadId = older.Id, ContentHierarchyItemId = node.Id },
            new DownloadScope { DownloadId = latest.Id, ContentHierarchyItemId = node.Id },
            new DownloadScope { DownloadId = incomplete.Id, ContentHierarchyItemId = node.Id });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new DownloadQuery { ContentHierarchyItemIds = [node.Id], Status = DownloadStatus.Imported },
            new PagingParameter { PageNumber = 1, PageSize = 1 },
            new SortParameter { PropertyName = "DateCompleted", Direction = SortDirection.Descending })
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items.Single().ExternalId, Is.EqualTo("latest"));
            Assert.That(result.TotalItems, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task BulkDeleteAsync_DeletesMatchingRowsAndReturnsCount_WhenFiltersAreSet()
    {
        // Arrange
        var keep = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-keep",
            Title = "StillActive",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        var dropA = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-drop-a",
            Title = "DoneA",
            Status = DownloadStatus.Imported,
            CreatedAt = DateTime.UtcNow
        };
        var dropB = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-drop-b",
            Title = "DoneB",
            Status = DownloadStatus.Failed,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.AddRange(keep, dropA, dropB);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.BulkDeleteAsync(
            new() { Filter = new() { Statuses = [DownloadStatus.Imported, DownloadStatus.Failed] } },
            _userId).ConfigureAwait(false);

        // Assert
        var remaining = await _context.Downloads.AsNoTracking().ToListAsync().ConfigureAwait(false);
        var deletedIds = _publishedEvents
            .Where(e => e.Name == "download.deleted")
            .Select(e => e.Context["download.id"]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.Error, Is.Null);
            Assert.That(remaining, Has.Count.EqualTo(1));
            Assert.That(remaining[0].ExternalId, Is.EqualTo("ext-keep"));
            Assert.That(deletedIds, Is.EquivalentTo([dropA.Id.ToString("D"), dropB.Id.ToString("D")]));
        }
    }

    [Test]
    public async Task BulkDeleteAsync_HonorsClientFilter_WhenClientFilterIsSet()
    {
        // Arrange
        var otherClient = new DownloadClient
        {
            Name = "OtherClient",
            ProviderType = "sabnzbd",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(otherClient);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.Downloads.AddRange(
            new Download
            {
                DownloadClientId = _downloadClientId,
                ExternalId = "ext-mine",
                Title = "Mine",
                Status = DownloadStatus.Imported,
                CreatedAt = DateTime.UtcNow
            },
            new Download
            {
                DownloadClientId = otherClient.Id,
                ExternalId = "ext-other",
                Title = "Other",
                Status = DownloadStatus.Imported,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.BulkDeleteAsync(
            new()
            {
                Filter = new() { DownloadClientId = _downloadClientId, Statuses = [DownloadStatus.Imported] }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var remaining = await _context.Downloads.AsNoTracking().ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(remaining, Has.Count.EqualTo(1));
            Assert.That(remaining[0].ExternalId, Is.EqualTo("ext-other"));
        }
    }

    [Test]
    public async Task BulkDeleteAsync_ReturnsZeroAndWritesNoAudit_WhenNoMatches()
    {
        // Arrange
        _context.Downloads.Add(new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-active",
            Title = "Active",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.BulkDeleteAsync(
            new() { Filter = new() { Statuses = [DownloadStatus.Imported, DownloadStatus.Failed] } },
            _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.Zero);
            Assert.That(auditRecords, Is.Empty);
            Assert.That(_context.Downloads.Count(), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task BulkDeleteAsync_WritesOneAuditRecord_WhenMultipleDownloadsAreDeleted()
    {
        // Arrange
        _context.Downloads.AddRange(
            new Download
            {
                DownloadClientId = _downloadClientId,
                ExternalId = "ext-1",
                Title = "One",
                Status = DownloadStatus.Imported,
                CreatedAt = DateTime.UtcNow
            },
            new Download
            {
                DownloadClientId = _downloadClientId,
                ExternalId = "ext-2",
                Title = "Two",
                Status = DownloadStatus.Imported,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.BulkDeleteAsync(
            new() { Filter = new() { Statuses = [DownloadStatus.Imported] } }, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Download"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
            Assert.That(auditRecords[0].Detail, Does.Contain("Deleted 2"));
        }
    }

    [Test]
    public async Task BulkDeleteAsync_HonorsExcludeIds_WhenExcludeIdsAreSet()
    {
        // Arrange
        var dropA = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-a",
            Title = "A",
            Status = DownloadStatus.Imported,
            CreatedAt = DateTime.UtcNow
        };
        var keep = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-keep",
            Title = "Keep",
            Status = DownloadStatus.Imported,
            CreatedAt = DateTime.UtcNow
        };
        var dropB = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-b",
            Title = "B",
            Status = DownloadStatus.Failed,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.AddRange(dropA, keep, dropB);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.BulkDeleteAsync(
            new()
            {
                Filter = new()
                {
                    Statuses = [DownloadStatus.Imported, DownloadStatus.Failed],
                    ExcludeIds = [keep.Id]
                }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var remaining = await _context.Downloads.AsNoTracking().ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(remaining, Has.Count.EqualTo(1));
            Assert.That(remaining[0].ExternalId, Is.EqualTo("ext-keep"));
        }
    }

    [Test]
    public async Task QueryAsync_SortsByCamelCasePropertyName_WhenCamelCaseSortPropertyIsSet()
    {
        // Arrange
        var older = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-older",
            Title = "Older",
            Status = DownloadStatus.Grabbed,
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var newer = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-newer",
            Title = "Newer",
            Status = DownloadStatus.Grabbed,
            CreatedAt = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        _context.Downloads.AddRange(older, newer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "createdAt", Direction = SortDirection.Descending };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items, Has.ItemAt(0).Property(nameof(Download.Title)).EqualTo("Newer"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(Download.Title)).EqualTo("Older"));
        }
    }

    [Test]
    public async Task MarkFailedAsync_RetainsReleaseScope_WhenADownloadFails()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var container = new MetadataContainer { CreatedAt = now };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "test",
            Level = "episode",
            Slug = Guid.NewGuid().ToString("N"),
            Name = "Episode",
            Monitoring = true,
            CreatedAt = now
        };
        _context.ContentHierarchyItems.Add(hierarchyItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.ContentItems.Add(new ContentItem
        {
            ContentHierarchyItemId = hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = now
        });
        var release = new Release
        {
            Protocol = "torrent",
            Title = "Release",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = now,
            CreatedAt = now
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var download = new Download
        {
            ReleaseId = release.Id,
            DownloadClientId = _downloadClientId,
            ExternalId = "download-failed",
            Title = "Release",
            Status = DownloadStatus.Downloading,
            CreatedAt = now
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.ReleaseScopes.Add(new ReleaseScope
        {
            ReleaseId = release.Id,
            ContentHierarchyItemId = hierarchyItem.Id,
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var transitioned = await _subject.MarkFailedAsync(
            download.Id,
            DownloadFailureReason.ImportFailed,
            "Import failed",
            "ImportFailed",
            "Import failed",
            now,
            _userId,
            correlationId: null).ConfigureAwait(false);

        // Assert
        var scopeCount = await _context.ReleaseScopes.CountAsync(c => c.ReleaseId == release.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(transitioned, Is.True);
            Assert.That(scopeCount, Is.EqualTo(1));
        }
    }

    [Test]
    [TestCase(DownloadStatus.Cancelled)]
    [TestCase(DownloadStatus.Imported)]
    public async Task MarkFailedAsync_ReturnsFalseAndKeepsStatus_WhenDownloadIsTerminal(DownloadStatus status)
    {
        // Arrange
        var now = DateTime.UtcNow;
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "terminal-download",
            Title = "Terminal",
            Status = status,
            CreatedAt = now
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var transitioned = await _subject.MarkFailedAsync(
            download.Id,
            DownloadFailureReason.ImportFailed,
            "Import failed",
            "ImportFailed",
            "Import failed",
            now,
            _userId,
            correlationId: null).ConfigureAwait(false);

        // Assert
        var stored = await _context.Downloads.AsNoTracking().SingleAsync(d => d.Id == download.Id)
            .ConfigureAwait(false);
        var auditCount = await _context.AuditRecords.CountAsync(a => a.Operation == "ImportFailed")
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(transitioned, Is.False);
            Assert.That(stored.Status, Is.EqualTo(status));
            Assert.That(stored.FailureReason, Is.Null);
            Assert.That(auditCount, Is.Zero);
        }
    }

    [Test]
    [TestCase(DownloadStatus.Cancelled)]
    [TestCase(DownloadStatus.Imported)]
    [TestCase(DownloadStatus.Importing)]
    public async Task UpdateRefreshedStatusAsync_ReturnsFalseAndKeepsStatus_WhenDownloadIsTerminal(
        DownloadStatus status)
    {
        // Arrange
        var now = DateTime.UtcNow;
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "terminal-refresh",
            Title = "Terminal",
            Status = status,
            CreatedAt = now
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var applied = await _subject.UpdateRefreshedStatusAsync(
            download.Id,
            DownloadStatus.Downloading,
            "client says downloading",
            outputPath: null,
            dateCompleted: null,
            progress: 0.5m,
            lastProgressAt: now,
            heldByPolicy: false,
            failureReason: null,
            failedAt: null,
            now,
            _userId).ConfigureAwait(false);

        // Assert
        var stored = await _context.Downloads.AsNoTracking().SingleAsync(d => d.Id == download.Id)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied, Is.False);
            Assert.That(stored.Status, Is.EqualTo(status));
            Assert.That(stored.StatusReason, Is.Null);
        }
    }

    [Test]
    public async Task UpdateRefreshedStatusAsync_ReturnsTrueAndUpdates_WhenDownloadIsActive()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "active-refresh",
            Title = "Active",
            Status = DownloadStatus.Queued,
            CreatedAt = now
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var applied = await _subject.UpdateRefreshedStatusAsync(
            download.Id,
            DownloadStatus.Downloading,
            "client says downloading",
            outputPath: null,
            dateCompleted: null,
            progress: 0.5m,
            lastProgressAt: now,
            heldByPolicy: false,
            failureReason: null,
            failedAt: null,
            now,
            _userId).ConfigureAwait(false);

        // Assert
        var stored = await _context.Downloads.AsNoTracking().SingleAsync(d => d.Id == download.Id)
            .ConfigureAwait(false);
        var downloadEvents = _publishedEvents
            .Where(e => e.Name.StartsWith("download.", StringComparison.Ordinal))
            .ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied, Is.True);
            Assert.That(stored.Status, Is.EqualTo(DownloadStatus.Downloading));
            Assert.That(stored.Progress, Is.EqualTo(0.5m));
            Assert.That(downloadEvents, Has.Count.EqualTo(1));
            Assert.That(downloadEvents[0].Name, Is.EqualTo("download.updated"));
            Assert.That(downloadEvents[0].Context["download.id"], Is.EqualTo(download.Id.ToString("D")));
        }
    }

    [Test]
    public async Task QueryQueueEntriesAsync_ProjectsContentDisplayFieldsAndClientName_WhenRelationsExist()
    {
        // Arrange
        var parentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(parentContainer, container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var parent = new ContentHierarchyItem
        {
            MetadataContainerId = parentContainer.Id,
            ContentType = "test",
            Level = "root",
            Slug = Guid.NewGuid().ToString("N"),
            Name = "Breaking Bad",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(parent);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ParentId = parent.Id,
            ContentType = "test",
            Level = "level",
            Slug = Guid.NewGuid().ToString("N"),
            Name = "Episode 1",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(hierarchyItem);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "test",
            Ordinal = 0,
            Title = "Pilot",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var release = new Release
        {
            Protocol = "torrent",
            Title = $"Release-{Guid.NewGuid():N}",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            InfoUrl = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var nodeId = await _context.ContentItems
            .Where(c => c.Id == contentItem.Id)
            .Select(c => c.ContentHierarchyItemId)
            .FirstAsync().ConfigureAwait(false);
        _context.ReleaseScopes.Add(new ReleaseScope { ReleaseId = release.Id, ContentHierarchyItemId = nodeId, });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var chain = (
            ReleaseId: release.Id,
            HierarchyItemId: hierarchyItem.Id);
        var download = new Download
        {
            ReleaseId = chain.ReleaseId,
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-entry-1",
            Title = "Breaking.Bad.S01E01.1080p",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var releaseScope = await _context.ReleaseScopes.FirstAsync(c => c.ReleaseId == chain.ReleaseId)
            .ConfigureAwait(false);
        _context.DownloadScopes.Add(new DownloadScope
        {
            DownloadId = download.Id,
            ContentHierarchyItemId = releaseScope.ContentHierarchyItemId
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryQueueEntriesAsync(new DownloadQuery(),
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var entry = result.Items.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.ContentHierarchyName, Is.EqualTo("Episode 1"));
            Assert.That(entry.ContentMetadataTitle, Is.EqualTo("Pilot"));
            Assert.That(entry.ContentHierarchyItemId, Is.EqualTo(chain.HierarchyItemId));
            Assert.That(entry.ContentHierarchyParentId, Is.EqualTo(parent.Id));
            Assert.That(entry.DownloadClientName, Is.EqualTo("TestClient"));
            Assert.That(entry.ProviderType, Is.EqualTo("downloadclient-a"));
            Assert.That(entry.Title, Is.EqualTo("Breaking.Bad.S01E01.1080p"));
        }
    }

    [Test]
    public async Task QueryQueueEntriesAsync_ResolvesTitleViaScopeRows_WhenGrabTargetAndChildrenHaveTitles()
    {
        // Arrange
        var seasonContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var episodeContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(seasonContainer, episodeContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var season = new ContentHierarchyItem
        {
            MetadataContainerId = seasonContainer.Id,
            ContentType = "tv",
            Level = "season",
            Slug = Guid.NewGuid().ToString("N"),
            Name = "Season 1",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(season);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var episode = new ContentHierarchyItem
        {
            MetadataContainerId = episodeContainer.Id,
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = Guid.NewGuid().ToString("N"),
            Name = "Episode 1",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(episode);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var release = new Release
        {
            Protocol = "torrent",
            Title = "Show.S01.1080p",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var download = new Download
        {
            ReleaseId = release.Id,
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-season",
            Title = "Show.S01.1080p",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.DownloadScopes.AddRange(
            new DownloadScope
            {
                DownloadId = download.Id,
                ContentHierarchyItemId = season.Id,
            },
            new DownloadScope
            {
                DownloadId = download.Id,
                ContentHierarchyItemId = episode.Id,
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var entry = await _subject.GetQueueEntryByIdAsync(download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.ContentHierarchyItemId, Is.EqualTo(season.Id));
            Assert.That(entry.ContentHierarchyName, Is.EqualTo("Season 1"));
        }
    }

    [Test]
    public async Task QueryQueueEntriesAsync_LeavesContentFieldsNull_WhenTheDownloadHasNoContent()
    {
        // Arrange
        _context.Downloads.Add(new Download
        {
            ReleaseId = null,
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-orphan",
            Title = "Orphan.Release.Title",
            Status = DownloadStatus.Failed,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var entry = (await _subject.QueryQueueEntriesAsync(new DownloadQuery(),
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items
            .Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.ContentHierarchyName, Is.Null);
            Assert.That(entry.ContentHierarchyItemId, Is.Null);
            Assert.That(entry.ContentHierarchyParentId, Is.Null);
            Assert.That(entry.DownloadClientName, Is.EqualTo("TestClient"));
            Assert.That(entry.Title, Is.EqualTo("Orphan.Release.Title"));
        }
    }

    [Test]
    public async Task QueryQueueEntriesAsync_ProjectsContentFields_WhenReleaseReferenceWasCleanedUp()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "test",
            Level = "level",
            Slug = Guid.NewGuid().ToString("N"),
            Name = "Better Call Saul",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(hierarchyItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var release = new Release
        {
            Protocol = "torrent",
            Title = $"Release-{Guid.NewGuid():N}",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            InfoUrl = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var nodeId = await _context.ContentItems
            .Where(c => c.Id == contentItem.Id)
            .Select(c => c.ContentHierarchyItemId)
            .FirstAsync().ConfigureAwait(false);
        _context.ReleaseScopes.Add(new ReleaseScope { ReleaseId = release.Id, ContentHierarchyItemId = nodeId, });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var chain = (
            ReleaseId: release.Id,
            ContentItemId: contentItem.Id,
            HierarchyItemId: hierarchyItem.Id,
            Name: "Better Call Saul");
        var download = new Download
        {
            ReleaseId = null,
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-cleaned-release",
            Title = "Better.Call.Saul.S01E01.1080p",
            Status = DownloadStatus.Downloaded,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.Add(new DownloadScope
        {
            DownloadId = download.Id,
            ContentHierarchyItemId = chain.HierarchyItemId
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var entry = (await _subject.QueryQueueEntriesAsync(new DownloadQuery(),
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items
            .Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.ContentHierarchyName, Is.EqualTo("Better Call Saul"));
            Assert.That(entry.ContentHierarchyItemId, Is.EqualTo(chain.HierarchyItemId));
            Assert.That(entry.ReleaseInfoUrl, Is.Null);
            Assert.That(entry.DownloadClientName, Is.EqualTo("TestClient"));
        }
    }

    [Test]
    public async Task QueryQueueEntriesAsync_ProjectsPosterUrlAndReleaseInfoUrl_WhenReleaseMetadataExists()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            ProviderType = "test",
            CreatedAt = DateTime.UtcNow
        };
        contribution.Images.Add(new Image
        {
            Ordinal = 4,
            Type = "poster",
            Url = new Uri("https://images.example/poster.jpg")
        });
        contribution.Images.Add(new Image
        {
            Ordinal = 0,
            Type = "backdrop",
            Url = new Uri("https://images.example/backdrop.jpg")
        });
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "test",
            Level = "level",
            Slug = Guid.NewGuid().ToString("N"),
            Name = "Poster Type A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(hierarchyItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var release = new Release
        {
            Protocol = "torrent",
            Title = $"Release-{Guid.NewGuid():N}",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            InfoUrl = new Uri("https://indexer.example/release/42"),
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var nodeId = await _context.ContentItems
            .Where(c => c.Id == contentItem.Id)
            .Select(c => c.ContentHierarchyItemId)
            .FirstAsync().ConfigureAwait(false);
        _context.ReleaseScopes.Add(new ReleaseScope { ReleaseId = release.Id, ContentHierarchyItemId = nodeId, });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var chain = (
            ReleaseId: release.Id,
            ContentItemId: contentItem.Id,
            HierarchyItemId: hierarchyItem.Id,
            Name: "Poster Type A");
        var download = new Download
        {
            ReleaseId = chain.ReleaseId,
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-poster",
            Title = "Poster.Type-A.1080p",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.Add(new DownloadScope
        {
            DownloadId = download.Id,
            ContentHierarchyItemId = chain.HierarchyItemId
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var entry = (await _subject.QueryQueueEntriesAsync(new DownloadQuery(),
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items
            .Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.PosterUrl, Is.EqualTo("https://images.example/poster.jpg"));
            Assert.That(entry.ReleaseInfoUrl, Is.EqualTo("https://indexer.example/release/42"));
        }
    }

    [Test]
    public async Task QueryQueueEntriesAsync_LeavesPosterAndReleaseInfoUrlNull_WhenTheDownloadHasNoPoster()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "test",
            Level = "level",
            Slug = Guid.NewGuid().ToString("N"),
            Name = "No Poster",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(hierarchyItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var release = new Release
        {
            Protocol = "torrent",
            Title = $"Release-{Guid.NewGuid():N}",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            InfoUrl = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var nodeId = await _context.ContentItems
            .Where(c => c.Id == contentItem.Id)
            .Select(c => c.ContentHierarchyItemId)
            .FirstAsync().ConfigureAwait(false);
        _context.ReleaseScopes.Add(new ReleaseScope { ReleaseId = release.Id, ContentHierarchyItemId = nodeId, });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var noPoster = (
            ReleaseId: release.Id,
            ContentItemId: contentItem.Id,
            HierarchyItemId: hierarchyItem.Id,
            Name: "No Poster");
        _context.Downloads.AddRange(
            new Download
            {
                ReleaseId = null,
                DownloadClientId = _downloadClientId,
                ExternalId = "ext-orphan-poster",
                Title = "Orphan.Title",
                Status = DownloadStatus.Failed,
                CreatedAt = DateTime.UtcNow
            },
            new Download
            {
                ReleaseId = noPoster.ReleaseId,
                DownloadClientId = _downloadClientId,
                ExternalId = "ext-nopaint",
                Title = "NoPoster.Title",
                Status = DownloadStatus.Downloading,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var entries = (await _subject.QueryQueueEntriesAsync(new DownloadQuery(),
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false))
            .Items
            .ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries, Has.All.Property(nameof(DownloadQueueEntry.PosterUrl)).Null);
            Assert.That(entries, Has.All.Property(nameof(DownloadQueueEntry.ReleaseInfoUrl)).Null);
        }
    }

    [Test]
    public async Task GetQueueEntryByIdAsync_ProjectsPosterUrlAndReleaseInfoUrl_WhenReleaseMetadataExists()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            ProviderType = "test",
            CreatedAt = DateTime.UtcNow
        };
        contribution.Images.Add(new Image
        {
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://images.example/detail.jpg")
        });
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "test",
            Level = "level",
            Slug = Guid.NewGuid().ToString("N"),
            Name = "Detail Poster",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(hierarchyItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var release = new Release
        {
            Protocol = "torrent",
            Title = $"Release-{Guid.NewGuid():N}",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            InfoUrl = new Uri("https://indexer.example/release/99"),
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var nodeId = await _context.ContentItems
            .Where(c => c.Id == contentItem.Id)
            .Select(c => c.ContentHierarchyItemId)
            .FirstAsync().ConfigureAwait(false);
        _context.ReleaseScopes.Add(new ReleaseScope { ReleaseId = release.Id, ContentHierarchyItemId = nodeId, });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var chain = (
            ReleaseId: release.Id,
            ContentItemId: contentItem.Id,
            HierarchyItemId: hierarchyItem.Id,
            Name: "Detail Poster");
        var download = new Download
        {
            ReleaseId = chain.ReleaseId,
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-detail-poster",
            Title = "Detail.Poster.2160p",
            Status = DownloadStatus.Imported,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.Add(new DownloadScope
        {
            DownloadId = download.Id,
            ContentHierarchyItemId = chain.HierarchyItemId
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var entry = await _subject.GetQueueEntryByIdAsync(download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.PosterUrl, Is.EqualTo("https://images.example/detail.jpg"));
            Assert.That(entry.ReleaseInfoUrl, Is.EqualTo("https://indexer.example/release/99"));
        }
    }

    [Test]
    public async Task QueryQueueEntriesAsync_SortsByContentHierarchyName_WhenNameSortIsSet()
    {
        // Arrange
        var chains = new Dictionary<string, (Guid ReleaseId, Guid ContentItemId, Guid HierarchyItemId, string Name)>();
        foreach (var name in new[] { "Zebra", "Apple", "Mango" })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var hierarchyItem = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "test",
                Level = "level",
                Slug = Guid.NewGuid().ToString("N"),
                Name = name,
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(hierarchyItem);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var contentItem = new ContentItem
            {
                ContentHierarchyItemId = hierarchyItem.Id,
                Fulfillment = FulfillmentStatus.Missing,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentItems.Add(contentItem);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var release = new Release
            {
                Protocol = "torrent",
                Title = $"Release-{Guid.NewGuid():N}",
                ExternalId = Guid.NewGuid().ToString("N"),
                PublishDate = DateTime.UtcNow,
                InfoUrl = null,
                CreatedAt = DateTime.UtcNow
            };
            _context.Releases.Add(release);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var nodeId = await _context.ContentItems
                .Where(c => c.Id == contentItem.Id)
                .Select(c => c.ContentHierarchyItemId)
                .FirstAsync().ConfigureAwait(false);
            _context.ReleaseScopes.Add(new ReleaseScope { ReleaseId = release.Id, ContentHierarchyItemId = nodeId, });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            chains.Add(name, (release.Id, contentItem.Id, hierarchyItem.Id, name));
        }

        var zebra = chains["Zebra"];
        var apple = chains["Apple"];
        var mango = chains["Mango"];

        var zebraDownload = new Download
        {
            ReleaseId = zebra.ReleaseId,
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-z",
            Title = "aaa-release",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        var appleDownload = new Download
        {
            ReleaseId = apple.ReleaseId,
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-a",
            Title = "zzz-release",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        var mangoDownload = new Download
        {
            ReleaseId = mango.ReleaseId,
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-m",
            Title = "mmm-release",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.AddRange(zebraDownload, appleDownload, mangoDownload);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.AddRange(
            new DownloadScope
            {
                DownloadId = zebraDownload.Id,
                ContentHierarchyItemId = zebra.HierarchyItemId
            },
            new DownloadScope
            {
                DownloadId = appleDownload.Id,
                ContentHierarchyItemId = apple.HierarchyItemId
            },
            new DownloadScope
            {
                DownloadId = mangoDownload.Id,
                ContentHierarchyItemId = mango.HierarchyItemId
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "ContentHierarchyName", Direction = SortDirection.Ascending };

        // Act
        var result = await _subject.QueryQueueEntriesAsync(new DownloadQuery(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(v => v.ContentHierarchyName), Is.EqualTo(["Apple", "Mango", "Zebra"]));
    }

    [Test]
    public async Task QueryQueueEntriesAsync_SortsByDateCompletedDescending_WhenCompletionDatesDiffer()
    {
        // Arrange
        _context.Downloads.AddRange(
            new Download
            {
                DownloadClientId = _downloadClientId,
                ExternalId = "ext-completed-entry",
                Title = "Completed",
                Status = DownloadStatus.Imported,
                DateCompleted = new DateTime(2024, 1, 14, 3, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2024, 1, 8, 3, 0, 0, DateTimeKind.Utc)
            },
            new Download
            {
                DownloadClientId = _downloadClientId,
                ExternalId = "ext-cancelled-entry",
                Title = "Cancelled",
                Status = DownloadStatus.Cancelled,
                DateCompleted = new DateTime(2024, 1, 11, 3, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2024, 1, 9, 3, 0, 0, DateTimeKind.Utc),
                ModifiedAt = new DateTime(2024, 1, 15, 3, 0, 0, DateTimeKind.Utc)
            },
            new Download
            {
                DownloadClientId = _downloadClientId,
                ExternalId = "ext-failed-entry",
                Title = "Failed",
                Status = DownloadStatus.Failed,
                DateCompleted = new DateTime(2024, 1, 12, 3, 0, 0, DateTimeKind.Utc),
                FailedAt = new DateTime(2024, 1, 16, 3, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2024, 1, 10, 3, 0, 0, DateTimeKind.Utc)
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "dateCompleted", Direction = SortDirection.Descending };

        // Act
        var result = await _subject.QueryQueueEntriesAsync(new DownloadQuery(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(i => i.Title), Is.EqualTo(["Failed", "Cancelled", "Completed"]));
    }

    [Test]
    public async Task QueryQueueEntriesAsync_SortsByProgress_WhenCamelCaseSortKeyIsProvided()
    {
        // Arrange
        var chains = new Dictionary<string, (Guid ReleaseId, Guid ContentItemId, Guid HierarchyItemId, string Name)>();
        foreach (var name in new[] { "Low", "High", "Mid" })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var hierarchyItem = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "test",
                Level = "level",
                Slug = Guid.NewGuid().ToString("N"),
                Name = name,
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(hierarchyItem);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var contentItem = new ContentItem
            {
                ContentHierarchyItemId = hierarchyItem.Id,
                Fulfillment = FulfillmentStatus.Missing,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentItems.Add(contentItem);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var release = new Release
            {
                Protocol = "torrent",
                Title = $"Release-{Guid.NewGuid():N}",
                ExternalId = Guid.NewGuid().ToString("N"),
                PublishDate = DateTime.UtcNow,
                InfoUrl = null,
                CreatedAt = DateTime.UtcNow
            };
            _context.Releases.Add(release);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var nodeId = await _context.ContentItems
                .Where(c => c.Id == contentItem.Id)
                .Select(c => c.ContentHierarchyItemId)
                .FirstAsync().ConfigureAwait(false);
            _context.ReleaseScopes.Add(new ReleaseScope { ReleaseId = release.Id, ContentHierarchyItemId = nodeId, });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            chains.Add(name, (release.Id, contentItem.Id, hierarchyItem.Id, name));
        }

        var low = chains["Low"];
        var high = chains["High"];
        var mid = chains["Mid"];

        _context.Downloads.AddRange(
            new Download
            {
                ReleaseId = low.ReleaseId,
                DownloadClientId = _downloadClientId,
                ExternalId = "ext-low",
                Title = "low",
                Status = DownloadStatus.Downloading,
                Progress = 10m,
                CreatedAt = DateTime.UtcNow
            },
            new Download
            {
                ReleaseId = high.ReleaseId,
                DownloadClientId = _downloadClientId,
                ExternalId = "ext-high",
                Title = "high",
                Status = DownloadStatus.Downloading,
                Progress = 90m,
                CreatedAt = DateTime.UtcNow
            },
            new Download
            {
                ReleaseId = mid.ReleaseId,
                DownloadClientId = _downloadClientId,
                ExternalId = "ext-mid",
                Title = "mid",
                Status = DownloadStatus.Downloading,
                Progress = 50m,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "progress", Direction = SortDirection.Descending };

        // Act
        var result = await _subject.QueryQueueEntriesAsync(new DownloadQuery(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(v => v.Progress), Is.EqualTo(new decimal?[] { 90m, 50m, 10m }));
    }

    [Test]
    public async Task GetQueueEntryByIdAsync_ReturnsEnrichedQueueEntry_WhenRelationsExist()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "test",
            Level = "level",
            Slug = Guid.NewGuid().ToString("N"),
            Name = "Dune",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(hierarchyItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var release = new Release
        {
            Protocol = "torrent",
            Title = $"Release-{Guid.NewGuid():N}",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            InfoUrl = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var nodeId = await _context.ContentItems
            .Where(c => c.Id == contentItem.Id)
            .Select(c => c.ContentHierarchyItemId)
            .FirstAsync().ConfigureAwait(false);
        _context.ReleaseScopes.Add(new ReleaseScope { ReleaseId = release.Id, ContentHierarchyItemId = nodeId, });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var chain = (
            ReleaseId: release.Id,
            ContentItemId: contentItem.Id,
            HierarchyItemId: hierarchyItem.Id,
            Name: "Dune");
        var download = new Download
        {
            ReleaseId = chain.ReleaseId,
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-detail",
            Title = "Dune.2021.2160p",
            Status = DownloadStatus.Imported,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.Add(new DownloadScope
        {
            DownloadId = download.Id,
            ContentHierarchyItemId = chain.HierarchyItemId
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var entry = await _subject.GetQueueEntryByIdAsync(download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.ContentHierarchyName, Is.EqualTo("Dune"));
            Assert.That(entry.ContentHierarchyItemId, Is.EqualTo(chain.HierarchyItemId));
            Assert.That(entry.DownloadClientName, Is.EqualTo("TestClient"));
        }
    }

    [Test]
    public async Task GetQueueEntryByIdAsync_ReturnsNull_WhenNotExists()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var result = await _subject.GetQueueEntryByIdAsync(id).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListStuckCandidatesAsync_ReturnsOnlyRowsPastTheirStateThreshold_WhenThresholdsHaveElapsed()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var downloadingAnchor = now.AddHours(-1);
        var stuckDownloading = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "stuck-downloading",
            Title = "StuckDownloading",
            Status = DownloadStatus.Downloading,
            LastProgressAt = downloadingAnchor
        };
        var stuckGrabbed = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "stuck-grabbed",
            Title = "StuckGrabbed",
            Status = DownloadStatus.Grabbed,
            LastProgressAt = now.AddHours(-1)
        };
        var stuckStalled = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "stuck-stalled",
            Title = "StuckStalled",
            Status = DownloadStatus.Stalled,
            LastProgressAt = now.AddHours(-3)
        };
        var stalledWithinThreshold = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "stalled-within",
            Title = "StalledWithin",
            Status = DownloadStatus.Stalled,
            LastProgressAt = now.AddHours(-1)
        };
        var freshDownloading = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "fresh-downloading",
            Title = "FreshDownloading",
            Status = DownloadStatus.Downloading,
            LastProgressAt = now.AddMinutes(-5)
        };
        await _subject.CreateAsync(stuckDownloading, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(stuckGrabbed, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(stuckStalled, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(stalledWithinThreshold, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(freshDownloading, _userId).ConfigureAwait(false);

        // Act
        var result = await _subject.ListStuckCandidatesAsync(now.AddMinutes(-30), now.AddHours(-2))
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Select(c => c.DownloadId),
                Is.EquivalentTo([stuckDownloading.Id, stuckGrabbed.Id, stuckStalled.Id]));
            Assert.That(
                result.Single(c => c.DownloadId == stuckDownloading.Id).InactivityAnchor,
                Is.EqualTo(downloadingAnchor));
        }
    }

    [Test]
    public async Task ListStuckCandidatesAsync_AnchorsOnCreatedAt_WhenLastProgressAtIsNull()
    {
        // Arrange
        var now = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var agedTime = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero));
        var agedRepository = new DownloadRepository(
            _context,
            new AuditRecordRepository(
                _context,
                Mock.Of<IEventBus>(),
                NullLoggerFactory.Instance.CreateLogger<AuditRecordRepository>(),
                new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
                Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IEventBus>(),
            NullLoggerFactory.Instance.CreateLogger<DownloadRepository>(),
            _protector.Object,
            agedTime);
        var aged = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "aged-no-progress",
            Title = "AgedNoProgress",
            Status = DownloadStatus.Downloading,
            LastProgressAt = null
        };
        await agedRepository.CreateAsync(aged, _userId, default).ConfigureAwait(false);
        var fresh = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "fresh-no-progress",
            Title = "FreshNoProgress",
            Status = DownloadStatus.Downloading,
            LastProgressAt = null
        };
        await _subject.CreateAsync(fresh, _userId).ConfigureAwait(false);

        // Act
        var result = await _subject.ListStuckCandidatesAsync(now.AddMinutes(-30), now.AddHours(-2))
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].DownloadId, Is.EqualTo(aged.Id));
            Assert.That(result[0].InactivityAnchor, Is.EqualTo(aged.CreatedAt));
        }
    }

    [Test]
    public async Task ListStuckCandidatesAsync_ExcludesPausedRows_WhenPausedProgressIsOld()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var paused = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "paused-old",
            Title = "PausedOld",
            Status = DownloadStatus.Paused,
            LastProgressAt = now.AddHours(-5)
        };
        await _subject.CreateAsync(paused, _userId).ConfigureAwait(false);

        // Act
        var result = await _subject.ListStuckCandidatesAsync(now.AddMinutes(-30), now.AddHours(-2))
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListStuckCandidatesAsync_ExcludesUnhealthyAndStaleClients_WhenClientHealthVaries()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var unhealthyClient = new DownloadClient
        {
            Name = "UnhealthyClient",
            ProviderType = "test",
            Enabled = true,
            Status = IntegrationHealthStatus.Unhealthy,
            CreatedAt = now
        };
        var staleClient = new DownloadClient
        {
            Name = "StaleClient",
            ProviderType = "test",
            Enabled = true,
            Status = IntegrationHealthStatus.Stale,
            CreatedAt = now
        };
        var unknownClient = new DownloadClient
        {
            Name = "UnknownClient",
            ProviderType = "test",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = now
        };
        _context.DownloadClients.AddRange(unhealthyClient, staleClient, unknownClient);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var onUnhealthy = new Download
        {
            DownloadClientId = unhealthyClient.Id,
            ExternalId = "on-unhealthy",
            Title = "OnUnhealthy",
            Status = DownloadStatus.Downloading,
            LastProgressAt = now.AddHours(-1)
        };
        var onStale = new Download
        {
            DownloadClientId = staleClient.Id,
            ExternalId = "on-stale",
            Title = "OnStale",
            Status = DownloadStatus.Downloading,
            LastProgressAt = now.AddHours(-1)
        };
        var onUnknown = new Download
        {
            DownloadClientId = unknownClient.Id,
            ExternalId = "on-unknown",
            Title = "OnUnknown",
            Status = DownloadStatus.Downloading,
            LastProgressAt = now.AddHours(-1)
        };
        await _subject.CreateAsync(onUnhealthy, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(onStale, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(onUnknown, _userId).ConfigureAwait(false);

        // Act
        var result = await _subject.ListStuckCandidatesAsync(now.AddMinutes(-30), now.AddHours(-2))
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].DownloadId, Is.EqualTo(onUnknown.Id));
            Assert.That(result[0].ClientName, Is.EqualTo("UnknownClient"));
        }
    }

    [Test]
    public async Task ListFailedByReleaseIdentitiesAsync_ReturnsExactFailedIdentityMatches_WhenIdentitiesAreProvided()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var recentFailed = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "taint-recent",
            Title = "RecentFailed",
            Status = DownloadStatus.Failed,
            FailedAt = now.AddDays(-1),
            ReleaseIdentityKeys = { new ReleaseIdentityKey { Authority = "hash", Value = "recent" } }
        };
        var agedFailed = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "taint-aged",
            Title = "AgedFailed",
            Status = DownloadStatus.Failed,
            FailedAt = now.AddDays(-400),
            ReleaseIdentityKeys = { new ReleaseIdentityKey { Authority = "guid", Value = "aged" } }
        };
        var noFailedAt = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "taint-no-failed-at",
            Title = "NoFailedAt",
            Status = DownloadStatus.Failed,
            FailedAt = null,
            ReleaseIdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "no-failed-at" } }
        };
        var crossPaired = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "cross-paired",
            Title = "CrossPaired",
            Status = DownloadStatus.Failed,
            FailedAt = now,
            ReleaseIdentityKeys = { new ReleaseIdentityKey { Authority = "hash", Value = "aged" } }
        };
        await _subject.CreateAsync(recentFailed, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(agedFailed, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(noFailedAt, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(crossPaired, _userId).ConfigureAwait(false);

        // Act
        var result = await _subject.ListFailedByReleaseIdentitiesAsync(
            [
                new ReleaseIdentityKey { Authority = "hash", Value = "recent" },
                new ReleaseIdentityKey { Authority = "guid", Value = "aged" },
                new ReleaseIdentityKey { Authority = "provider", Value = "no-failed-at" }
            ],
            null,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Select(d => d.Id), Is.EquivalentTo([recentFailed.Id, agedFailed.Id, noFailedAt.Id]));
    }

    [Test]
    public async Task ListFailedByReleaseIdentitiesAsync_ExcludesImportedAndCancelledRows_WhenKeysMatch()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var imported = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "taint-imported",
            Title = "Imported",
            Status = DownloadStatus.Imported,
            DateCompleted = now.AddDays(-1),
            ReleaseIdentityKeys = { new ReleaseIdentityKey { Authority = "hash", Value = "imported" } }
        };
        var cancelled = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "taint-cancelled",
            Title = "Cancelled",
            Status = DownloadStatus.Cancelled,
            ReleaseIdentityKeys = { new ReleaseIdentityKey { Authority = "hash", Value = "cancelled" } }
        };
        await _subject.CreateAsync(imported, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(cancelled, _userId).ConfigureAwait(false);

        // Act
        var result = await _subject.ListFailedByReleaseIdentitiesAsync(
            [
                new ReleaseIdentityKey { Authority = "hash", Value = "imported" },
                new ReleaseIdentityKey { Authority = "hash", Value = "cancelled" }
            ],
            null,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListCleanupCandidatesAsync_ReturnsUnstampedTerminalRows_WhenRemovalTimestampsDiffer()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var failed = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "cleanup-failed",
            Title = "Failed",
            Status = DownloadStatus.Failed,
            FailedAt = now.AddDays(-1)
        };
        var imported = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "cleanup-imported",
            Title = "Imported",
            Status = DownloadStatus.Imported,
            DateCompleted = now.AddDays(-1)
        };
        var alreadyStamped = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "cleanup-stamped",
            Title = "Stamped",
            Status = DownloadStatus.Failed,
            FailedAt = now.AddDays(-1),
            ClientRemovedAt = now.AddHours(-1)
        };
        var active = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "cleanup-active",
            Title = "Active",
            Status = DownloadStatus.Downloading
        };
        await _subject.CreateAsync(failed, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(imported, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(alreadyStamped, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(active, _userId).ConfigureAwait(false);

        // Act
        var result = await _subject.ListCleanupCandidatesAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result.Select(c => c.DownloadId), Is.EquivalentTo([failed.Id, imported.Id]));
    }

    [Test]
    public async Task ListCleanupCandidatesAsync_ExcludesRows_WhenParentClientIsUnhealthy()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var unhealthyClient = new DownloadClient
        {
            Name = "UnhealthyClient",
            ProviderType = "test",
            Enabled = true,
            Status = IntegrationHealthStatus.Unhealthy,
            CreatedAt = now
        };
        _context.DownloadClients.Add(unhealthyClient);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var onHealthy = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "cleanup-healthy",
            Title = "OnHealthy",
            Status = DownloadStatus.Failed,
            FailedAt = now.AddDays(-1)
        };
        var onUnhealthy = new Download
        {
            DownloadClientId = unhealthyClient.Id,
            ExternalId = "cleanup-unhealthy",
            Title = "OnUnhealthy",
            Status = DownloadStatus.Failed,
            FailedAt = now.AddDays(-1)
        };
        await _subject.CreateAsync(onHealthy, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(onUnhealthy, _userId).ConfigureAwait(false);

        // Act
        var result = await _subject.ListCleanupCandidatesAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].DownloadId, Is.EqualTo(onHealthy.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersAndSortsByLastChangedAt_WhenTimestampCriteriaAreSet()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var min = now.AddHours(-3);
        var max = now.AddHours(-1);
        var createdAtMin = new Download
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            DownloadClientId = _downloadClientId,
            ExternalId = "created-at-min",
            Title = "Created at min",
            Status = DownloadStatus.Importing,
            CreatedAt = min,
            ModifiedAt = null
        };
        var modifiedAtMin = new Download
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            DownloadClientId = _downloadClientId,
            ExternalId = "modified-at-min",
            Title = "Modified at min",
            Status = DownloadStatus.Downloaded,
            CreatedAt = now.AddHours(-8),
            ModifiedAt = min
        };
        var modifiedAtMax = new Download
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
            DownloadClientId = _downloadClientId,
            ExternalId = "modified-at-max",
            Title = "Modified at max",
            Status = DownloadStatus.Downloaded,
            CreatedAt = now.AddHours(-8),
            ModifiedAt = max
        };
        var beforeMin = new Download
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
            DownloadClientId = _downloadClientId,
            ExternalId = "before-min",
            Title = "Before min",
            Status = DownloadStatus.Downloaded,
            CreatedAt = now.AddHours(-8),
            ModifiedAt = min.AddMinutes(-1)
        };
        var afterMax = new Download
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000005"),
            DownloadClientId = _downloadClientId,
            ExternalId = "after-max",
            Title = "After max",
            Status = DownloadStatus.Importing,
            CreatedAt = now.AddHours(-8),
            ModifiedAt = max.AddMinutes(1)
        };
        var wrongStatus = new Download
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000006"),
            DownloadClientId = _downloadClientId,
            ExternalId = "wrong-status",
            Title = "Wrong status",
            Status = DownloadStatus.Imported,
            CreatedAt = now.AddHours(-8),
            ModifiedAt = min.AddHours(1)
        };
        _context.Downloads.AddRange(createdAtMin, modifiedAtMin, modifiedAtMax, beforeMin, afterMax, wrongStatus);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new DownloadQuery
            {
                Statuses = [DownloadStatus.Downloaded, DownloadStatus.Importing],
                LastChangedAt = new Range<DateTime>(min, max)
            },
            new() { PageNumber = 1, PageSize = 100 },
            new()
            {
                PropertyName = nameof(DownloadQuery.LastChangedAt),
                Direction = SortDirection.Ascending
            }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Items.Select(download => download.Id),
                Is.EqualTo([createdAtMin.Id, modifiedAtMin.Id, modifiedAtMax.Id]));
            Assert.That(result.TotalItems, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task DeleteClearedFailedDownloadsAsync_DeletesOnlyAgedFailures_WhenAgeAndStatusVary()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var agedFailed = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "aged-failed",
            Title = "AgedFailed",
            Status = DownloadStatus.Failed,
            FailureReason = DownloadFailureReason.ImportFailed,
            FailedAt = now.AddDays(-31)
        };
        var freshFailed = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "fresh-failed",
            Title = "FreshFailed",
            Status = DownloadStatus.Failed,
            FailureReason = DownloadFailureReason.ImportFailed,
            FailedAt = now.AddDays(-1)
        };
        var cancelled = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "old-cancelled",
            Title = "OldCancelled",
            Status = DownloadStatus.Cancelled
        };
        var imported = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "old-imported",
            Title = "OldImported",
            Status = DownloadStatus.Imported,
            DateCompleted = now.AddDays(-31)
        };
        await _subject.CreateAsync(agedFailed, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(freshFailed, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(cancelled, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(imported, _userId).ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteClearedFailedDownloadsAsync(now.AddDays(-30)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(await _context.Downloads.AnyAsync(d => d.Id == agedFailed.Id).ConfigureAwait(false), Is.False);
            Assert.That(await _context.Downloads.AnyAsync(d => d.Id == freshFailed.Id).ConfigureAwait(false), Is.True);
            Assert.That(await _context.Downloads.AnyAsync(d => d.Id == cancelled.Id).ConfigureAwait(false), Is.True);
            Assert.That(await _context.Downloads.AnyAsync(d => d.Id == imported.Id).ConfigureAwait(false), Is.True);
        }
    }

    [Test]
    public async Task DeleteClearedFailedDownloadsAsync_AnchorsOnCreatedAt_WhenFailedAtIsNull()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var agedTime = new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(-31));
        var agedRepository = new DownloadRepository(
            _context,
            new AuditRecordRepository(
                _context,
                Mock.Of<IEventBus>(),
                NullLoggerFactory.Instance.CreateLogger<AuditRecordRepository>(),
                new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
                Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IEventBus>(),
            NullLoggerFactory.Instance.CreateLogger<DownloadRepository>(),
            _protector.Object,
            agedTime);
        var agedNoTimestamp = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "aged-no-timestamp",
            Title = "AgedNoTimestamp",
            Status = DownloadStatus.Failed,
            FailureReason = DownloadFailureReason.ImportFailed,
            FailedAt = null
        };
        await agedRepository.CreateAsync(agedNoTimestamp, _userId, default).ConfigureAwait(false);
        var freshNoTimestamp = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "fresh-no-timestamp",
            Title = "FreshNoTimestamp",
            Status = DownloadStatus.Failed,
            FailureReason = DownloadFailureReason.ImportFailed,
            FailedAt = null
        };
        await _subject.CreateAsync(freshNoTimestamp, _userId).ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteClearedFailedDownloadsAsync(now.AddDays(-30)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(
                await _context.Downloads.AnyAsync(d => d.Id == agedNoTimestamp.Id).ConfigureAwait(false),
                Is.False);
            Assert.That(
                await _context.Downloads.AnyAsync(d => d.Id == freshNoTimestamp.Id).ConfigureAwait(false),
                Is.True);
        }
    }

    [Test]
    public async Task DeleteClearedFailedDownloadsAsync_DeletesFailedRow_WhenSuccessfulAlternativeLanded()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "test",
            Level = "level",
            Slug = Guid.NewGuid().ToString("N"),
            Name = $"Item-{Guid.NewGuid():N}",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(hierarchyItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var releaseIds = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var release = new Release
            {
                Protocol = "torrent",
                Title = $"Release-{Guid.NewGuid():N}",
                ExternalId = Guid.NewGuid().ToString("N"),
                PublishDate = DateTime.UtcNow,
                InfoUrl = null,
                CreatedAt = DateTime.UtcNow
            };
            _context.Releases.Add(release);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var nodeId = await _context.ContentItems
                .Where(c => c.Id == contentItem.Id)
                .Select(c => c.ContentHierarchyItemId)
                .FirstAsync().ConfigureAwait(false);
            _context.ReleaseScopes.Add(new ReleaseScope { ReleaseId = release.Id, ContentHierarchyItemId = nodeId, });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            releaseIds.Add(release.Id);
        }

        var failedReleaseId = releaseIds[0];
        var importedReleaseId = releaseIds[1];
        var failed = new Download
        {
            ReleaseId = failedReleaseId,
            DownloadClientId = _downloadClientId,
            ExternalId = "alt-failed",
            Title = "Failed",
            Status = DownloadStatus.Failed,
            FailureReason = DownloadFailureReason.DownloadErrored,
            FailedAt = now.AddDays(-2)
        };
        var imported = new Download
        {
            ReleaseId = importedReleaseId,
            DownloadClientId = _downloadClientId,
            ExternalId = "alt-imported",
            Title = "SuccessfulAlternative",
            Status = DownloadStatus.Imported,
            DateCompleted = now.AddDays(-1)
        };
        await _subject.CreateAsync(failed, _userId).ConfigureAwait(false);
        await _subject.CreateAsync(imported, _userId).ConfigureAwait(false);
        var failedReleaseScope = await _context.ReleaseScopes.FirstAsync(c => c.ReleaseId == failedReleaseId)
            .ConfigureAwait(false);
        _context.DownloadScopes.Add(new DownloadScope
        {
            DownloadId = failed.Id,
            ContentHierarchyItemId = failedReleaseScope.ContentHierarchyItemId
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var importedReleaseScope = await _context.ReleaseScopes.FirstAsync(c => c.ReleaseId == importedReleaseId)
            .ConfigureAwait(false);
        _context.DownloadScopes.Add(new DownloadScope
        {
            DownloadId = imported.Id,
            ContentHierarchyItemId = importedReleaseScope.ContentHierarchyItemId
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteClearedFailedDownloadsAsync(now.AddDays(-30)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(await _context.Downloads.AnyAsync(d => d.Id == failed.Id).ConfigureAwait(false), Is.False);
            Assert.That(await _context.Downloads.AnyAsync(d => d.Id == imported.Id).ConfigureAwait(false), Is.True);
        }
    }

    [Test]
    public async Task DeleteClearedFailedDownloadsAsync_KeepsFailedRow_WhenNoAlternativeLandedWithinWindow()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "test",
            Level = "level",
            Slug = Guid.NewGuid().ToString("N"),
            Name = $"Item-{Guid.NewGuid():N}",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(hierarchyItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var release = new Release
        {
            Protocol = "torrent",
            Title = $"Release-{Guid.NewGuid():N}",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            InfoUrl = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var nodeId = await _context.ContentItems
            .Where(c => c.Id == contentItem.Id)
            .Select(c => c.ContentHierarchyItemId)
            .FirstAsync().ConfigureAwait(false);
        _context.ReleaseScopes.Add(new ReleaseScope { ReleaseId = release.Id, ContentHierarchyItemId = nodeId, });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var releaseId = release.Id;
        var failed = new Download
        {
            ReleaseId = releaseId,
            DownloadClientId = _downloadClientId,
            ExternalId = "no-alt-failed",
            Title = "Failed",
            Status = DownloadStatus.Failed,
            FailureReason = DownloadFailureReason.DownloadErrored,
            FailedAt = now.AddDays(-2)
        };
        await _subject.CreateAsync(failed, _userId).ConfigureAwait(false);
        var releaseScope = await _context.ReleaseScopes.FirstAsync(c => c.ReleaseId == releaseId).ConfigureAwait(false);
        _context.DownloadScopes.Add(new DownloadScope
        {
            DownloadId = failed.Id,
            ContentHierarchyItemId = releaseScope.ContentHierarchyItemId
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteClearedFailedDownloadsAsync(now.AddDays(-30)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.Zero);
            Assert.That(await _context.Downloads.AnyAsync(d => d.Id == failed.Id).ConfigureAwait(false), Is.True);
        }
    }

    [Test]
    public async Task DeleteClearedFailedDownloadsAsync_DeletesOrphanFailedRow_WhenAgedPastWindow()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var orphan = new Download
        {
            ReleaseId = null,
            DownloadClientId = _downloadClientId,
            ExternalId = "orphan-failed",
            Title = "OrphanFailed",
            Status = DownloadStatus.Failed,
            FailureReason = DownloadFailureReason.ImportFailed,
            FailedAt = now.AddDays(-45)
        };
        await _subject.CreateAsync(orphan, _userId).ConfigureAwait(false);
        _publishedEvents.Clear();

        // Act
        var deleted = await _subject.DeleteClearedFailedDownloadsAsync(now.AddDays(-30)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(await _context.Downloads.AnyAsync(d => d.Id == orphan.Id).ConfigureAwait(false), Is.False);
            Assert.That(_publishedEvents, Has.Count.EqualTo(1));
            Assert.That(_publishedEvents[0].Name, Is.EqualTo("download.deleted"));
            Assert.That(_publishedEvents[0].Context["download.id"], Is.EqualTo(orphan.Id.ToString("D")));
        }
    }

    [Test]
    public async Task BulkPatchAsync_UpdatesSelectedDownloads_WhenSelectionMatches()
    {
        // Arrange
        var selected = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "selected",
            Title = "Selected",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        var other = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "other",
            Title = "Other",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.AddRange(selected, other);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.BulkPatchAsync(
            new()
            {
                Ids = [selected.Id],
                Patch = new()
                {
                    Status = new() { Value = DownloadStatus.Paused },
                    StatusReason = new() { Value = "Operator pause" }
                }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var downloads = await _context.Downloads.AsNoTracking().OrderBy(download => download.ExternalId)
            .ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(
                downloads.Single(download => download.Id == selected.Id).Status,
                Is.EqualTo(DownloadStatus.Paused));
            Assert.That(
                downloads.Single(download => download.Id == other.Id).Status,
                Is.EqualTo(DownloadStatus.Downloading));
        }
    }

    [Test]
    public async Task MarkImportingAsync_SetsStatusImporting_WhenDownloaded()
    {
        // Arrange
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-importing",
            Title = "Importing",
            Status = DownloadStatus.Downloaded,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var transitioned = await _subject.MarkImportingAsync(download.Id, DateTime.UtcNow, _userId)
            .ConfigureAwait(false);

        // Assert
        var stored = await _context.Downloads.AsNoTracking().SingleAsync(d => d.Id == download.Id)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(transitioned, Is.True);
            Assert.That(stored.Status, Is.EqualTo(DownloadStatus.Importing));
            Assert.That(_publishedEvents, Has.Count.EqualTo(1));
            Assert.That(_publishedEvents[0].Name, Is.EqualTo("download.updated"));
            Assert.That(_publishedEvents[0].Context["download.changedFields"], Is.EqualTo("status"));
        }
    }

    [Test]
    public async Task MarkImportingAsync_LeavesStatusUnchanged_WhenNotDownloaded()
    {
        // Arrange
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-imported",
            Title = "Imported",
            Status = DownloadStatus.Imported,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var transitioned = await _subject.MarkImportingAsync(download.Id, DateTime.UtcNow, _userId)
            .ConfigureAwait(false);

        // Assert
        var stored = await _context.Downloads.AsNoTracking().SingleAsync(d => d.Id == download.Id)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(transitioned, Is.False);
            Assert.That(stored.Status, Is.EqualTo(DownloadStatus.Imported));
        }
    }

    [Test]
    public async Task MarkImportedAsync_SetsStatusImported_WhenImporting()
    {
        // Arrange
        var completedAt = DateTime.UtcNow;
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-cycle",
            Title = "Cycle",
            Status = DownloadStatus.Importing,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.MarkImportedAsync(download.Id, null, 0, completedAt, DateTime.UtcNow, _userId)
            .ConfigureAwait(false);

        // Assert
        var stored = await _context.Downloads.AsNoTracking().SingleAsync(d => d.Id == download.Id)
            .ConfigureAwait(false);
        var downloadEvents = _publishedEvents
            .Where(e => e.Name.StartsWith("download.", StringComparison.Ordinal))
            .ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored.Status, Is.EqualTo(DownloadStatus.Imported));
            Assert.That(downloadEvents, Has.Count.EqualTo(1));
            Assert.That(downloadEvents[0].Name, Is.EqualTo("download.imported"));
        }
    }

    [Test]
    public async Task UpdateActiveRefreshedStatusAsync_LeavesImportingUnchanged_WhenClientStillReportsDownloaded()
    {
        // Arrange
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-refresh",
            Title = "Refresh",
            Status = DownloadStatus.Importing,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var applied = await _subject.UpdateActiveRefreshedStatusAsync(
            download.Id,
            DownloadStatus.Downloaded,
            statusReason: null,
            outputPath: "/data/refresh",
            dateCompleted: DateTime.UtcNow,
            progress: 1m,
            lastProgressAt: DateTime.UtcNow,
            heldByPolicy: false,
            failureReason: null,
            failedAt: null,
            now: DateTime.UtcNow,
            userId: _userId).ConfigureAwait(false);

        // Assert
        var stored = await _context.Downloads.AsNoTracking().SingleAsync(d => d.Id == download.Id)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied, Is.False);
            Assert.That(stored.Status, Is.EqualTo(DownloadStatus.Importing));
        }
    }

    [Test]
    public async Task UpdateActiveRefreshedStatusAsync_PublishesSingleStalledEvent_WhenStatusTransitions()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "ext-active-stalled",
            Title = "Active stalled",
            Status = DownloadStatus.Downloading,
            CreatedAt = now
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var applied = await _subject.UpdateActiveRefreshedStatusAsync(
            download.Id,
            DownloadStatus.Stalled,
            statusReason: "client stalled",
            outputPath: null,
            dateCompleted: null,
            progress: 0.5m,
            lastProgressAt: now,
            heldByPolicy: false,
            failureReason: null,
            failedAt: null,
            now,
            _userId).ConfigureAwait(false);

        // Assert
        var downloadEvents = _publishedEvents
            .Where(e => e.Name.StartsWith("download.", StringComparison.Ordinal))
            .ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied, Is.True);
            Assert.That(downloadEvents, Has.Count.EqualTo(1));
            Assert.That(downloadEvents[0].Name, Is.EqualTo("download.stalled"));
            Assert.That(downloadEvents[0].Context["download.stallReason"], Is.EqualTo("client stalled"));
        }
    }

    [TestCase(DownloadStatus.Downloaded, "download.completed")]
    [TestCase(DownloadStatus.Failed, "download.failed")]
    [TestCase(DownloadStatus.Stalled, "download.stalled")]
    public async Task UpdateRefreshedStatusAsync_PublishesSingleSemanticEvent_WhenStatusTransitions(
        DownloadStatus status,
        string expectedEventName)
    {
        // Arrange
        var now = DateTime.UtcNow;
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = $"ext-{status}",
            Title = $"Transition {status}",
            Status = DownloadStatus.Downloading,
            CreatedAt = now
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var applied = await _subject.UpdateRefreshedStatusAsync(
            download.Id,
            status,
            "client status",
            outputPath: null,
            dateCompleted: status == DownloadStatus.Downloaded ? now : null,
            progress: status == DownloadStatus.Downloaded ? 1m : 0.5m,
            lastProgressAt: now,
            heldByPolicy: false,
            failureReason: status == DownloadStatus.Failed ? DownloadFailureReason.DownloadErrored : null,
            failedAt: status == DownloadStatus.Failed ? now : null,
            now,
            _userId).ConfigureAwait(false);

        // Assert
        var downloadEvents = _publishedEvents
            .Where(e => e.Name.StartsWith("download.", StringComparison.Ordinal))
            .ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied, Is.True);
            Assert.That(downloadEvents, Has.Count.EqualTo(1));
            Assert.That(downloadEvents[0].Name, Is.EqualTo(expectedEventName));
            Assert.That(downloadEvents[0].Context["download.id"], Is.EqualTo(download.Id.ToString("D")));
        }
    }

    [Test]
    public async Task ListImportedByContentHierarchyItemAsync_ReturnsMatchingImports_WhenInsideTheWindow()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var container = new MetadataContainer { CreatedAt = now };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var episode = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = Guid.NewGuid().ToString("N"),
            Name = "Episode 1",
            CreatedAt = now
        };
        _context.ContentHierarchyItems.Add(episode);
        var within = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "within",
            Title = "pack within window",
            Status = DownloadStatus.Imported,
            DateCompleted = now,
            CreatedAt = now,
            ReleaseIdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "pack-key" } }
        };
        var stale = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "stale",
            Title = "pack before window",
            Status = DownloadStatus.Imported,
            DateCompleted = now.AddHours(-48),
            CreatedAt = now.AddHours(-48)
        };
        _context.Downloads.AddRange(within, stale);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.AddRange(
            new DownloadScope { DownloadId = within.Id, ContentHierarchyItemId = episode.Id },
            new DownloadScope { DownloadId = stale.Id, ContentHierarchyItemId = episode.Id });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var downloads = await _subject
            .ListImportedByContentHierarchyItemAsync(episode.Id, now.AddHours(-12))
            .ConfigureAwait(false);

        // Assert
        var identity = downloads[0].ReleaseIdentityKeys.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(downloads.Select(download => download.Id), Is.EqualTo([within.Id]));
            Assert.That(downloads[0].ReleaseIdentityKeys, Has.Count.EqualTo(1));
            Assert.That(identity.Authority, Is.EqualTo("provider"));
            Assert.That(identity.Value, Is.EqualTo("pack-key"));
        }
    }

    [Test]
    public async Task ListClientResidentByContentItemsAsync_LoadsTheScopes_WhenADownloadIsResident()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "test",
            Level = "level",
            Slug = Guid.NewGuid().ToString("N"),
            Name = $"Item-{Guid.NewGuid():N}",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(hierarchyItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            ExternalId = "resident-ext",
            Title = "Resident",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.Add(new DownloadScope
        {
            DownloadId = download.Id,
            ContentHierarchyItemId = hierarchyItem.Id
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListClientResidentByContentItemsAsync([contentItem.Id]).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Select(item => item.Id), Is.EqualTo([download.Id]));
            Assert.That(
                result[0].Scope.Select(scope => scope.ContentHierarchyItemId),
                Is.EqualTo([hierarchyItem.Id]));
        }
    }

    [TestCase("posterUrl")]
    [TestCase("providerType")]
    [TestCase("contentMetadataTitle")]
    [TestCase("contentHierarchyItemId")]
    [TestCase("contentHierarchyParentId")]
    [TestCase("releaseInfoUrl")]
    [TestCase("canPause")]
    [TestCase("canResume")]
    [TestCase("canForceStart")]
    [TestCase("scope")]
    public async Task QueryQueueEntriesAsync_ReturnsPage_WhenSortIsNotMapped(string propertyName)
    {
        // Arrange
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            Title = "Queue entry",
            ExternalId = "queue-entry"
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page = await _subject.QueryQueueEntriesAsync(
            new(), new() { PageNumber = 1, PageSize = 10 },
            new() { PropertyName = propertyName, Direction = SortDirection.Ascending })
            .ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Select(d => d.Title), Is.EqualTo(["Queue entry"]));
    }

    [Test]
    public async Task BulkPatchAsync_RollsBackAllRows_WhenAuditFails()
    {
        // Arrange
        _context.Downloads.AddRange(
            new Download
            {
                DownloadClientId = _downloadClientId,
                Title = "First",
                ExternalId = "first",
                Status = DownloadStatus.Downloading
            },
            new Download
            {
                DownloadClientId = _downloadClientId,
                Title = "Second",
                ExternalId = "second",
                Status = DownloadStatus.Downloading
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var audit = new Mock<IAuditRecordRepository>();
        audit.Setup(a => a.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Audit failed."));
        var subject = new DownloadRepository(_context, audit.Object, _transactionManager,
            Mock.Of<IEventBus>(), NullLogger<DownloadRepository>.Instance, _protector.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(async () => await subject.BulkPatchAsync(
            new() { Filter = new(), Patch = new() { Status = new() { Value = DownloadStatus.Cancelled } } },
            _userId).ConfigureAwait(false));

        // Assert
        var statuses = await _context.Downloads.AsNoTracking().Select(d => d.Status).ToListAsync()
            .ConfigureAwait(false);
        Assert.That(statuses, Is.EqualTo([DownloadStatus.Downloading, DownloadStatus.Downloading]));
    }

    [Test]
    public async Task BulkPatchAsync_WritesAuditForEachDownload_WhenMultipleRowsMatch()
    {
        // Arrange
        _context.Downloads.AddRange(
            new Download
            {
                Id = Guid.Parse("db6fb62d-844b-4db7-9834-77f06b3d6846"),
                DownloadClientId = _downloadClientId,
                Title = "First",
                ExternalId = "first",
                Status = DownloadStatus.Downloading
            },
            new Download
            {
                Id = Guid.Parse("0f30ee1a-43c9-4d33-b295-cf82f21ab143"),
                DownloadClientId = _downloadClientId,
                Title = "Second",
                ExternalId = "second",
                Status = DownloadStatus.Downloading
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.BulkPatchAsync(
            new() { Filter = new(), Patch = new() { Status = new() { Value = DownloadStatus.Cancelled } } },
            _userId).ConfigureAwait(false);

        // Assert
        var audits = await _context.AuditRecords.AsNoTracking().OrderBy(a => a.Detail).ToListAsync()
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(audits.Select(a => a.EntityId), Is.EqualTo(
            [
                "db6fb62d-844b-4db7-9834-77f06b3d6846", "0f30ee1a-43c9-4d33-b295-cf82f21ab143"
            ]));
            Assert.That(audits.Select(a => a.Detail), Is.EqualTo(
            [
                "Updated download First.", "Updated download Second."
            ]));
            Assert.That(audits.Select(a => a.EntityType), Is.All.EqualTo("Download"));
            Assert.That(audits.Select(a => a.Operation), Is.All.EqualTo("Patch"));
            Assert.That(audits.Select(a => a.UserId), Is.All.EqualTo(_userId));
            Assert.That(_publishedEvents, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task PatchAsync_WritesAuditWithTitle_WhenDownloadExists()
    {
        // Arrange
        var download = new Download
        {
            Id = Guid.Parse("db6fb62d-844b-4db7-9834-77f06b3d6846"),
            DownloadClientId = _downloadClientId,
            Title = "First",
            ExternalId = "first",
            Status = DownloadStatus.Downloading
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.PatchAsync(
            new() { Id = download.Id, Status = new() { Value = DownloadStatus.Cancelled } },
            _userId).ConfigureAwait(false);

        // Assert
        var audit = await _context.AuditRecords.AsNoTracking().SingleAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(audit.EntityId, Is.EqualTo("db6fb62d-844b-4db7-9834-77f06b3d6846"));
            Assert.That(audit.Detail, Is.EqualTo("Updated download First."));
            Assert.That(audit.EntityType, Is.EqualTo("Download"));
            Assert.That(audit.Operation, Is.EqualTo("Patch"));
            Assert.That(audit.UserId, Is.EqualTo(_userId));
            Assert.That(_publishedEvents, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task DeleteClearedFailedDownloadsAsync_RollsBackDeletion_WhenAuditFails()
    {
        // Arrange
        _context.Downloads.Add(new Download
        {
            DownloadClientId = _downloadClientId,
            Title = "Failed",
            ExternalId = "failed",
            Status = DownloadStatus.Failed,
            CreatedAt = new DateTime(2026, 1, 1)
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var audit = new Mock<IAuditRecordRepository>();
        audit.Setup(a => a.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Audit failed."));
        var subject = new DownloadRepository(_context, audit.Object, _transactionManager,
            Mock.Of<IEventBus>(), NullLogger<DownloadRepository>.Instance, _protector.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(async () => await subject.DeleteClearedFailedDownloadsAsync(
            new DateTime(2026, 2, 1)).ConfigureAwait(false));

        // Assert
        Assert.That(await _context.Downloads.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ListCandidatesAsync_DecryptsSettings_WhenClientIsProtected(bool stuck)
    {
        // Arrange
        _context.Downloads.Add(new Download
        {
            DownloadClientId = _downloadClientId,
            Title = "Candidate",
            ExternalId = "candidate",
            Status = stuck ? DownloadStatus.Downloading : DownloadStatus.Failed,
            CreatedAt = new DateTime(2026, 1, 1)
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _protector.Setup(p => p.Unprotect(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(new Dictionary<string, string> { ["password"] = "clear-value" });

        // Act
        var settings = stuck
            ? (await _subject.ListStuckCandidatesAsync(new DateTime(2026, 2, 1), new DateTime(2026, 2, 1))
                .ConfigureAwait(false)).Single().Settings
            : (await _subject.ListCleanupCandidatesAsync().ConfigureAwait(false)).Single().Settings;

        // Assert
        Assert.That(settings?["password"], Is.EqualTo("clear-value"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ListCandidatesAsync_ReportsNullSettings_WhenSettingsCannotBeRead(bool stuck)
    {
        // Arrange
        _context.Downloads.Add(new Download
        {
            DownloadClientId = _downloadClientId,
            Title = "Candidate",
            ExternalId = "candidate",
            Status = stuck ? DownloadStatus.Downloading : DownloadStatus.Failed,
            CreatedAt = new DateTime(2026, 1, 1)
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _protector.Setup(p => p.Unprotect(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Throws(new InvalidOperationException("Unreadable settings."));

        // Act
        var settings = stuck
            ? (await _subject.ListStuckCandidatesAsync(new DateTime(2026, 2, 1), new DateTime(2026, 2, 1))
                .ConfigureAwait(false)).Single().Settings
            : (await _subject.ListCleanupCandidatesAsync().ConfigureAwait(false)).Single().Settings;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings, Is.Null);
            Assert.That(await _context.Downloads.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task MarkImportingAsync_RollsBackDownload_WhenAcquisitionWriteFails()
    {
        // Arrange
        var node = new ContentHierarchyItem
        {
            MetadataContainer = new(),
            ContentType = "test",
            Level = "item",
            Slug = "test",
            Name = "Test",
            ContentItem = new() { ActiveAcquisitionStatus = DownloadStatus.Downloaded }
        };
        var download = new Download
        {
            DownloadClientId = _downloadClientId,
            Title = "Downloaded",
            ExternalId = "downloaded",
            Status = DownloadStatus.Downloaded
        };
        _context.ContentHierarchyItems.Add(node);
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.Add(new() { DownloadId = download.Id, ContentHierarchyItemId = node.Id });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        await _context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER acquisition_failure BEFORE UPDATE OF ActiveAcquisitionStatus ON content_item
            BEGIN
                SELECT RAISE(ABORT, 'Acquisition write failed.');
            END;
            """).ConfigureAwait(false);

        // Act
        Assert.ThrowsAsync<SqliteException>(async () =>
            await _subject.MarkImportingAsync(download.Id, DateTime.UtcNow, _userId).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Downloads.AsNoTracking().Select(d => d.Status).SingleAsync()
                .ConfigureAwait(false), Is.EqualTo(DownloadStatus.Downloaded));
            Assert.That(await _context.ContentItems.AsNoTracking().Select(c => c.ActiveAcquisitionStatus)
                .SingleAsync().ConfigureAwait(false), Is.EqualTo(DownloadStatus.Downloaded));
            Assert.That(_publishedEvents, Is.Empty);
        }
    }
}
