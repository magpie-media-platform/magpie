using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Enums;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Retention;

[TestFixture]
public sealed class RetentionCleanupJobTests
{
    private readonly IProgressReporter _progressReporter = Mock.Of<IProgressReporter>();

    private Mock<IJobExecutionRepository> _jobExecutionRepository = null!;
    private Mock<IAuditRecordRepository> _auditRecordRepository = null!;
    private Mock<IReleaseRepository> _releaseRepository = null!;
    private Mock<IDownloadService> _downloadService = null!;
    private DateTime? _jobExecutionCutoff;
    private DateTime? _auditRecordCutoff;
    private DateTime? _releaseCutoff;
    private DateTime? _failedDownloadCutoff;
    private IJob _subject = null!;
    private JobContext _jobContext = null!;

    [SetUp]
    public void SetUp()
    {
        _jobExecutionCutoff = null;
        _auditRecordCutoff = null;
        _releaseCutoff = null;
        _failedDownloadCutoff = null;

        _jobExecutionRepository = new Mock<IJobExecutionRepository>();
        _jobExecutionRepository
            .Setup(r => r.DeleteWhereAsync(It.IsAny<JobExecutionQuery>(), It.IsAny<CancellationToken>()))
            .Callback((JobExecutionQuery query, CancellationToken _) => _jobExecutionCutoff = query.CompletedAt?.Max)
            .ReturnsAsync(0);
        _auditRecordRepository = new Mock<IAuditRecordRepository>();
        _auditRecordRepository
            .Setup(r => r.DeleteWhereAsync(
                It.IsAny<AuditRecordQuery>(), It.IsAny<CancellationToken>()))
            .Callback((AuditRecordQuery query, CancellationToken _) =>
                _auditRecordCutoff = query.TimestampBefore)
            .ReturnsAsync(0);
        _releaseRepository = new Mock<IReleaseRepository>();
        _releaseRepository
            .Setup(r => r.DeleteWhereAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<CancellationToken>()))
            .Callback((ReleaseQuery query, CancellationToken _) => _releaseCutoff = query.ObservedAt?.Max)
            .ReturnsAsync(0);
        _downloadService = new Mock<IDownloadService>();
        _downloadService
            .Setup(r => r.DeleteClearedFailedDownloadsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback((DateTime failedBefore, CancellationToken _) => _failedDownloadCutoff = failedBefore)
            .ReturnsAsync(0);

        _subject = new RetentionCleanupJob(
            _jobExecutionRepository.Object,
            _auditRecordRepository.Object,
            _releaseRepository.Object,
            _downloadService.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<RetentionCleanupJob>.Instance,
            _progressReporter);
        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);
    }

    [Test]
    public async Task ExecuteAsync_DeletesAgedRows_WhenNoRetentionOverrideIsSet()
    {
        // Arrange
        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_jobExecutionCutoff, Is.EqualTo(new DateTime(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc)));
            Assert.That(_auditRecordCutoff, Is.EqualTo(new DateTime(2026, 2, 19, 12, 0, 0, DateTimeKind.Utc)));
            Assert.That(_releaseCutoff, Is.EqualTo(new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc)));
            Assert.That(_failedDownloadCutoff, Is.EqualTo(new DateTime(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc)));
        }
    }

    [Test]
    public async Task ExecuteAsync_HonorsParameterOverride_WhenJobExecutionRetentionIsOverridden()
    {
        // Arrange
        var jobContext = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["jobExecutionRetentionDays"] = "5"
            }
        };

        // Act
        await _subject.ExecuteAsync(jobContext).ConfigureAwait(false);

        // Assert
        Assert.That(_jobExecutionCutoff, Is.EqualTo(new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task ExecuteAsync_UsesTheCurrentTimeAsCutoff_WhenRetentionDaysIsZero()
    {
        // Arrange
        var jobContext = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["jobExecutionRetentionDays"] = "0"
            }
        };

        // Act
        await _subject.ExecuteAsync(jobContext).ConfigureAwait(false);

        // Assert
        Assert.That(_jobExecutionCutoff, Is.EqualTo(new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task ExecuteAsync_HonorsParameterOverride_WhenFailedDownloadRetentionIsOverridden()
    {
        // Arrange
        var jobContext = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["failedDownloadRetentionDays"] = "1"
            }
        };

        // Act
        await _subject.ExecuteAsync(jobContext).ConfigureAwait(false);

        // Assert
        Assert.That(_failedDownloadCutoff, Is.EqualTo(new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task ExecuteAsync_HonorsZeroRetentionDays_WhenFailedDownloadRetentionIsZero()
    {
        // Arrange
        var jobContext = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["failedDownloadRetentionDays"] = "0"
            }
        };

        // Act
        await _subject.ExecuteAsync(jobContext).ConfigureAwait(false);

        // Assert
        Assert.That(_failedDownloadCutoff, Is.EqualTo(new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc)));
    }
}
