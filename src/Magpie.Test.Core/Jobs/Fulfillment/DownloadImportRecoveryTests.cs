using System;
using System.Collections.Generic;
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
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class DownloadImportRecoveryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private Download _download = null!;
    private DownloadImportScanJob _scan = null!;
    private JobContext _jobContext = null!;

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
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var executions = new JobExecutionRepository(
            _context,
            Mock.Of<IAuditRecordRepository>(),
            transactions,
            Mock.Of<IEventBus>(),
            NullLogger<JobExecutionRepository>.Instance,
            time,
            new JobRegistry([], new JobRuntimeOptions(), NullLogger<JobRegistry>.Instance));
        var registry = Mock.Of<IJobRegistry>(registry =>
            registry.ListJobSpecs() == new[] { DownloadImportItemJob.Spec });
        var manager = new JobManager(Mock.Of<IJobRuntime>(), registry, executions,
            Mock.Of<IScheduleTriggerRepository>(), Mock.Of<IEventTriggerRepository>(),
            Mock.Of<IJobConcurrencyScheduler>(), Mock.Of<IJobExecutionScopeFactory>(), time,
            NullLogger<JobManager>.Instance, transactions);
        _download = new Download
        {
            Id = new Guid("abcdefff-1234-5678-90ab-cdef12345678"),
            Title = "Recovery",
            ExternalId = "recovery",
            Status = DownloadStatus.Importing
        };
        var downloads = new Mock<IDownloadRepository> { CallBase = true };
        downloads.Setup(repository => repository.QueryAsync(It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download> { Items = [_download] });
        _scan = new DownloadImportScanJob(downloads.Object, executions, manager, time,
            NullLogger<DownloadImportScanJob>.Instance, Mock.Of<IProgressReporter>());
        _jobContext = new JobContext(Guid.NewGuid(), null, null, null, JobExecutionMode.Queue,
            new Dictionary<string, string>(), null);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [TestCase(false, "D")]
    [TestCase(true, "D")]
    [TestCase(false, "N")]
    [TestCase(true, "B")]
    [TestCase(false, "P")]
    [TestCase(true, "X")]
    public async Task ExecuteAsync_QueuesPolicyOnce_WhenPriorAttemptWasInterrupted(bool fromEvent, string format)
    {
        // Arrange
        var execution = new JobExecution
        {
            JobType = "DownloadImportItem",
            Status = JobExecutionStatus.Cancelled,
            CreatedAt = new DateTime(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc),
            Parameters = new() { ["collisionMode"] = "replace" }
        };
        if (fromEvent)
        {
            execution.TriggerContext["event.context.download.id"] = _download.Id.ToString(format);
        }
        else
        {
            execution.Parameters["downloadId"] = _download.Id.ToString(format);
        }

        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _scan.ExecuteAsync(_jobContext).ConfigureAwait(false);
        await _scan.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var queued = await _context.JobExecutions.AsNoTracking()
            .Where(row => row.Status == JobExecutionStatus.Queued).ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(queued, Has.Count.EqualTo(1));
            Assert.That(queued[0].Parameters["collisionMode"], Is.EqualTo("replace"));
            Assert.That(queued[0].Parameters["downloadId"], Is.EqualTo(_download.Id.ToString("D")));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
        }
    }

    [TestCase(JobExecutionStatus.Queued)]
    [TestCase(JobExecutionStatus.Running)]
    [TestCase(JobExecutionStatus.CancellationRequested)]
    public async Task ExecuteAsync_QueuesNothing_WhenExistingEventExecutionRemainsActive(JobExecutionStatus status)
    {
        // Arrange
        _context.JobExecutions.Add(new JobExecution
        {
            JobType = "DownloadImportItem",
            Status = status,
            TriggerContext = new() { ["event.context.download.id"] = _download.Id.ToString("D") },
            CreatedAt = new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _scan.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_QueuesNothing_WhenActiveExecutionHasPaddedDownloadId(
        [Values(JobExecutionStatus.Queued, JobExecutionStatus.Running, JobExecutionStatus.CancellationRequested)]
        JobExecutionStatus status,
        [Values(false, true)] bool fromEvent,
        [Values(" \tabcdefff-1234-5678-90ab-cdef12345678\r\n",
            " { 0xabcdefff, 0x1234, 0x5678, { 0x90, 0xab, 0xcd, 0xef, 0x12, 0x34, 0x56, 0x78 } } ")]
        string rawId)
    {
        // Arrange
        var execution = new JobExecution
        {
            JobType = "DownloadImportItem",
            Status = status,
            CreatedAt = new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc)
        };
        if (fromEvent)
        {
            execution.TriggerContext["event.context.download.id"] = rawId;
        }
        else
        {
            execution.Parameters["downloadId"] = rawId;
        }

        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _scan.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_PreservesPolicy_WhenPriorExecutionHasPaddedDownloadId(
        [Values(false, true)] bool fromEvent,
        [Values(" \tabcdefff-1234-5678-90ab-cdef12345678\r\n",
            " { 0xabcdefff, 0x1234, 0x5678, { 0x90, 0xab, 0xcd, 0xef, 0x12, 0x34, 0x56, 0x78 } } ")]
        string rawId)
    {
        // Arrange
        var execution = new JobExecution
        {
            JobType = "DownloadImportItem",
            Status = JobExecutionStatus.Cancelled,
            Parameters = new() { ["collisionMode"] = "replace" },
            CreatedAt = new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc)
        };
        if (fromEvent)
        {
            execution.TriggerContext["event.context.download.id"] = rawId;
        }
        else
        {
            execution.Parameters["downloadId"] = rawId;
        }

        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _scan.ExecuteAsync(_jobContext).ConfigureAwait(false);
        await _scan.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var queued = await _context.JobExecutions.AsNoTracking()
            .SingleAsync(execution => execution.Status == JobExecutionStatus.Queued).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(queued.Parameters, Contains.Key("collisionMode"));
            Assert.That(queued.Parameters.GetValueOrDefault("collisionMode"), Is.EqualTo("replace"));
            Assert.That(queued.Parameters.GetValueOrDefault("downloadId"),
                Is.EqualTo("abcdefff-1234-5678-90ab-cdef12345678"));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
        }
    }

    [TestCase(false, false, JobExecutionStatus.Queued, 1)]
    [TestCase(true, false, JobExecutionStatus.Queued, 1)]
    [TestCase(false, true, JobExecutionStatus.Queued, 0)]
    [TestCase(true, true, JobExecutionStatus.Queued, 0)]
    [TestCase(false, false, JobExecutionStatus.Running, 0)]
    [TestCase(true, false, JobExecutionStatus.Running, 0)]
    [TestCase(false, false, JobExecutionStatus.CancellationRequested, 0)]
    [TestCase(true, false, JobExecutionStatus.CancellationRequested, 0)]
    public async Task ExecuteAsync_CoalescesRecovery_WhenPaddedOwnerArrivesAfterActiveRead(
        bool fromEvent, bool deferred, JobExecutionStatus status, int expectedClaimable)
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var repository = new JobExecutionRepository(
            _context,
            Mock.Of<IAuditRecordRepository>(),
            transactions,
            Mock.Of<IEventBus>(),
            NullLogger<JobExecutionRepository>.Instance,
            time,
            new JobRegistry([], new JobRuntimeOptions(), NullLogger<JobRegistry>.Instance));
        var registry = Mock.Of<IJobRegistry>(registry =>
            registry.ListJobSpecs() == new[] { DownloadImportItemJob.Spec });
        var manager = new JobManager(Mock.Of<IJobRuntime>(), registry, repository,
            Mock.Of<IScheduleTriggerRepository>(), Mock.Of<IEventTriggerRepository>(),
            Mock.Of<IJobConcurrencyScheduler>(), Mock.Of<IJobExecutionScopeFactory>(), time,
            NullLogger<JobManager>.Instance, transactions);
        var observed = new Mock<IJobExecutionRepository> { CallBase = true };
        observed.Setup(executions => executions.QueryAsync(It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .Returns((JobExecutionQuery query, PagingParameter paging, SortParameter? sort, CancellationToken token) =>
                repository.QueryAsync(query, paging, sort, token));
        observed.Setup(executions => executions.QueryAsync(
                It.Is<JobExecutionQuery>(query => query.Statuses != null),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .Returns(async (JobExecutionQuery query, PagingParameter paging, SortParameter? sort,
                CancellationToken token) =>
            {
                var result = await repository.QueryAsync(query, paging, sort, token).ConfigureAwait(false);
                var ownerId = await manager.ExecuteAsync(new JobEnqueueRequest
                {
                    JobType = "DownloadImportItem",
                    Mode = JobExecutionMode.Queue,
                    NotBeforeAt = deferred ? new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc) : null,
                    Parameters = fromEvent ? null : new Dictionary<string, string>
                    {
                        ["downloadId"] = " abcdefff-1234-5678-90ab-cdef12345678 "
                    },
                    TriggerContext = fromEvent ? new Dictionary<string, string>
                    {
                        ["event.context.download.id"] = " abcdefff-1234-5678-90ab-cdef12345678 "
                    } : null
                }, token).ConfigureAwait(false);
                var owner = await _context.JobExecutions.SingleAsync(execution => execution.Id == ownerId, token)
                    .ConfigureAwait(false);
                owner.Status = status;
                await _context.SaveChangesAsync(token).ConfigureAwait(false);
                return result;
            });
        var downloads = new Mock<IDownloadRepository> { CallBase = true };
        downloads.Setup(repository => repository.QueryAsync(It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download> { Items = [_download] });
        var scan = new DownloadImportScanJob(downloads.Object, observed.Object, manager, time,
            NullLogger<DownloadImportScanJob>.Instance, Mock.Of<IProgressReporter>());

        // Act
        await scan.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var persisted = await _context.JobExecutions.AsNoTracking().SingleAsync().ConfigureAwait(false);
        var claimable = await repository.GetClaimableCandidatesAsync(
            new PagingParameter { PageNumber = 1, PageSize = 20 }, default).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted.Status, Is.EqualTo(status));
            Assert.That(persisted.NotBeforeAt,
                Is.EqualTo(deferred ? new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc) : (DateTime?)null));
            Assert.That(claimable, Has.Count.EqualTo(expectedClaimable));
        }
    }

    [TestCase(JobExecutionStatus.Running, "00000000-0000-0000-0000-000000000001")]
    [TestCase(JobExecutionStatus.Cancelled, "00000000-0000-0000-0000-000000000001")]
    [TestCase(JobExecutionStatus.Running, "invalid")]
    [TestCase(JobExecutionStatus.Cancelled, "invalid")]
    public async Task ExecuteAsync_UsesParameterIdentity_WhenEventReferencesDifferentDownload(
        JobExecutionStatus status, string parameter)
    {
        // Arrange
        _context.JobExecutions.Add(new JobExecution
        {
            JobType = "DownloadImportItem",
            Status = status,
            Parameters = new() { ["downloadId"] = parameter, ["collisionMode"] = "replace" },
            TriggerContext = new() { ["event.context.download.id"] = "abcdefff-1234-5678-90ab-cdef12345678" },
            CreatedAt = new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _scan.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var queued = await _context.JobExecutions.AsNoTracking()
            .SingleAsync(execution => execution.Status == JobExecutionStatus.Queued).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(queued.Parameters, Does.Not.ContainKey("collisionMode"));
            Assert.That(queued.Parameters.GetValueOrDefault("downloadId"),
                Is.EqualTo("abcdefff-1234-5678-90ab-cdef12345678"));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
        }
    }
}
