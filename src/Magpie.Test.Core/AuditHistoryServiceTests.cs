using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Config;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Runtime;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class AuditHistoryServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid ContentId = new("00000000-0000-0000-0000-0000000000c1");

    private static readonly Guid DownloadId = new("00000000-0000-0000-0000-0000000000d1");

    private static readonly Guid CancelledExecutionId = new("00000000-0000-0000-0000-0000000000e1");

    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private Mock<IDownloadRepository> _downloads = null!;
    private IAuditHistoryService _subject = null!;
    private Guid _userId;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        using var context = new MagpieDbContext(options.UseSqlite(_template).Options);
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
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        _context = new MagpieDbContext(options.UseSqlite(connection, contextOwnsConnection: true).Options);

        var user = new User
        {
            Username = "magpie-admin",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = Now
        };
        _context.Users.Add(user);
        _context.SaveChanges();
        _userId = user.Id;

        _context.AuditRecords.AddRange(
            new AuditRecord
            {
                Category = AuditCategories.Content,
                Operation = "Update",
                EntityId = ContentId.ToString("D"),
                Timestamp = Now.AddDays(-1)
            },
            new AuditRecord
            {
                Category = AuditCategories.Jobs,
                Operation = "Cancel",
                JobExecutionId = CancelledExecutionId,
                Timestamp = Now.AddDays(-2)
            },
            new AuditRecord { Category = AuditCategories.System, Operation = "Old10", Timestamp = Now.AddDays(-10) },
            new AuditRecord { Category = AuditCategories.System, Operation = "Old40", Timestamp = Now.AddDays(-40) },
            new AuditRecord { Category = AuditCategories.System, Operation = "Old100", Timestamp = Now.AddDays(-100) },
            new AuditRecord { Category = AuditCategories.System, Operation = "Old400", Timestamp = Now.AddDays(-400) });
        _context.JobExecutions.AddRange(
            new JobExecution
            {
                Id = CancelledExecutionId,
                JobType = "DownloadImportItem",
                Status = JobExecutionStatus.Completed,
                Parameters = new() { ["downloadId"] = DownloadId.ToString("D") },
                CreatedAt = Now.AddDays(-2),
                CompletedAt = Now.AddDays(-1).AddHours(-1)
            },
            new JobExecution
            {
                JobType = "Finished10",
                Status = JobExecutionStatus.Completed,
                CreatedAt = Now.AddDays(-10),
                CompletedAt = Now.AddDays(-10)
            },
            new JobExecution
            {
                JobType = "Finished40",
                Status = JobExecutionStatus.Failed,
                CreatedAt = Now.AddDays(-40),
                CompletedAt = Now.AddDays(-40)
            },
            new JobExecution
            {
                JobType = "Finished100",
                Status = JobExecutionStatus.Cancelled,
                CreatedAt = Now.AddDays(-100),
                CompletedAt = Now.AddDays(-100)
            },
            new JobExecution
            {
                JobType = "Finished400",
                Status = JobExecutionStatus.Completed,
                CreatedAt = Now.AddDays(-400),
                CompletedAt = Now.AddDays(-400)
            },
            new JobExecution
            {
                JobType = "Queued400",
                Status = JobExecutionStatus.Queued,
                CreatedAt = Now.AddDays(-400)
            },
            new JobExecution
            {
                JobType = "Running400",
                Status = JobExecutionStatus.Running,
                CreatedAt = Now.AddDays(-400),
                StartedAt = Now.AddDays(-400)
            },
            new JobExecution
            {
                JobType = "Cancelling400",
                Status = JobExecutionStatus.CancellationRequested,
                CreatedAt = Now.AddDays(-400),
                StartedAt = Now.AddDays(-400)
            });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        _downloads = new Mock<IDownloadRepository>();
        _downloads
            .Setup(repository => repository.QueryAsync(
                It.Is<DownloadQuery>(query => query.ContentHierarchyItemIds!.Single() == ContentId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items =
                [
                    new Download
                    {
                        Id = DownloadId,
                        DownloadClientId = Guid.NewGuid(),
                        ExternalId = "download-1",
                        Title = "The Matrix",
                        Status = DownloadStatus.Imported,
                        CreatedAt = Now.AddDays(-2)
                    }
                ]
            });

        var time = new FakeTimeProvider(Now);
        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var jobRegistry = new JobRegistry(
            [new(DownloadImportItemJob.Spec, typeof(DownloadImportItemJob))],
            new JobRuntimeOptions(),
            NullLogger<JobRegistry>.Instance);
        var auditRecords = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            transactionManager,
            Mock.Of<IJobExecutionScopeFactory>(),
            time);
        _subject = new AuditHistoryService(
            new AuditHistoryRepository(_context, jobRegistry, NullLogger<AuditHistoryRepository>.Instance),
            auditRecords,
            new JobExecutionRepository(
                _context,
                auditRecords,
                transactionManager,
                Mock.Of<IEventBus>(),
                NullLogger<JobExecutionRepository>.Instance,
                time,
                jobRegistry),
            _downloads.Object,
            transactionManager,
            time);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task QueryAsync_ReturnsTheContentEntries_WhenTheQueryHasAContent()
    {
        // Arrange
        var query = new AuditHistoryQuery { Paging = new() { PageNumber = 1, PageSize = 25 }, ContentId = ContentId };

        // Act
        var page = await _subject.QueryAsync(query).ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Select(entry => entry.Label), Is.EqualTo(["Update", "DownloadImportItem"]));
    }

    [Test]
    public async Task QueryAsync_ReturnsTheJobExecutionEntries_WhenTheQueryHasAJobExecution()
    {
        // Arrange
        var query = new AuditHistoryQuery
        {
            Paging = new() { PageNumber = 1, PageSize = 25 },
            JobExecutionId = CancelledExecutionId
        };

        // Act
        var page = await _subject.QueryAsync(query).ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Select(entry => entry.Label), Is.EqualTo(["Cancel"]));
    }

    [Test]
    public async Task QueryAsync_ReturnsEveryEntry_WhenTheQueryHasNoScope()
    {
        // Arrange
        var query = new AuditHistoryQuery { Paging = new() { PageNumber = 1, PageSize = 25 } };

        // Act
        var page = await _subject.QueryAsync(query).ConfigureAwait(false);

        // Assert
        Assert.That(page.TotalItems, Is.EqualTo(14));
    }

    [Test]
    public async Task QueryAsync_DoesNotReadTheDownloads_WhenTheContentQueryIsForAuditRecordsOnly()
    {
        // Arrange
        var query = new AuditHistoryQuery
        {
            Paging = new() { PageNumber = 1, PageSize = 25 },
            ContentId = ContentId,
            Kind = AuditHistoryEntryKind.AuditRecord
        };

        // Act
        var page = await _subject.QueryAsync(query).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items.Select(entry => entry.Label), Is.EqualTo(["Update"]));
            _downloads.Verify(
                repository => repository.QueryAsync(
                    It.IsAny<DownloadQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(8)]
    [TestCase(31)]
    [TestCase(364)]
    [TestCase(366)]
    public async Task PurgeAsync_Throws_WhenTheAgeIsNotAPurgeChoice(int olderThanDays)
    {
        // Arrange
        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await _subject.PurgeAsync(olderThanDays, _userId).ConfigureAwait(false),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(await _context.AuditRecords.CountAsync().ConfigureAwait(false), Is.EqualTo(6));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(8));
        }
    }

    [TestCase(7, 4, 4, 3, 4)]
    [TestCase(30, 3, 3, 4, 5)]
    [TestCase(90, 2, 2, 5, 6)]
    [TestCase(365, 1, 1, 6, 7)]
    public async Task PurgeAsync_ReturnsTheRemovedCounts_WhenTheAgeIsAPurgeChoice(
        int olderThanDays,
        int expectedAuditRecords,
        int expectedJobExecutions,
        int expectedRemainingAuditRecords,
        int expectedRemainingJobExecutions)
    {
        // Arrange
        // Act
        var purged = await _subject.PurgeAsync(olderThanDays, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(purged.AuditRecords, Is.EqualTo(expectedAuditRecords));
            Assert.That(purged.JobExecutions, Is.EqualTo(expectedJobExecutions));
            Assert.That(
                await _context.AuditRecords.CountAsync().ConfigureAwait(false),
                Is.EqualTo(expectedRemainingAuditRecords));
            Assert.That(
                await _context.JobExecutions.CountAsync().ConfigureAwait(false),
                Is.EqualTo(expectedRemainingJobExecutions));
        }
    }

    [Test]
    public async Task PurgeAsync_KeepsUnfinishedJobExecutions_WhenTheyAreOlderThanTheAge()
    {
        // Arrange
        // Act
        await _subject.PurgeAsync(7, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(
            await _context.JobExecutions
                .OrderBy(execution => execution.JobType)
                .Select(execution => execution.JobType)
                .ToListAsync()
                .ConfigureAwait(false),
            Is.EqualTo(["Cancelling400", "DownloadImportItem", "Queued400", "Running400"]));
    }

    [Test]
    public async Task PurgeAsync_WritesAnAuditRecord_WhenThePurgeCompletes()
    {
        // Arrange
        // Act
        await _subject.PurgeAsync(30, _userId).ConfigureAwait(false);

        // Assert
        var record = await _context.AuditRecords
            .SingleAsync(auditRecord => auditRecord.Operation == "ActivityLogPurged")
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.Category, Is.EqualTo("System"));
            Assert.That(record.UserId, Is.EqualTo(_userId));
            Assert.That(record.Timestamp, Is.EqualTo(Now));
            Assert.That(record.Detail, Is.EqualTo("Purged 3 events and 3 job runs from before Sep 7, 2026."));
        }
    }

    [Test]
    public async Task ListByContentAsync_ReturnsTheRecordsOfTheCategoryNewestFirst_WhenRecordsNameTheContent()
    {
        // Arrange
        _context.AuditRecords.AddRange(
            new AuditRecord
            {
                Category = "Processing",
                Operation = "Older",
                EntityId = ContentId.ToString("D"),
                Timestamp = Now.AddHours(-2)
            },
            new AuditRecord
            {
                Category = "Processing",
                Operation = "Newer",
                EntityId = ContentId.ToString("D"),
                Timestamp = Now.AddHours(-1)
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var records = await _subject.ListByContentAsync(ContentId, "Processing").ConfigureAwait(false);

        // Assert
        Assert.That(records.Select(record => record.Operation), Is.EqualTo(["Newer", "Older"]));
    }
}
