using System;
using System.Collections.Generic;
using System.Linq;
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

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Retention;

[TestFixture]
public sealed class RetentionCleanupJobTests
{
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
            TimeProvider.System,
            NullLogger<RetentionCleanupJob>.Instance);
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
    public void Spec_DeclaresRetentionParameters_WithDefaults()
    {
        // Arrange
        // Act
        var parameters = RetentionCleanupJob.Spec.Parameters;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                parameters.Select(p => p.Name),
                Is.EqualTo(
                [
                    "jobExecutionRetentionDays",
                    "auditRetentionDays",
                    "releaseObservationRetentionDays",
                    "failedDownloadRetentionDays"
                ]));
            Assert.That(parameters.Select(p => p.Type), Is.All.EqualTo(DataType.Number));
            Assert.That(parameters.Select(p => p.Required), Is.All.EqualTo(false));
            Assert.That(parameters.Select(p => p.DefaultValue), Is.EqualTo(["30", "90", "7", "30"]));
        }
    }

    [Test]
    public async Task ExecuteAsync_DeletesAgedRows_WithDefaultRetentionWindows()
    {
        // Arrange
        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        _jobExecutionRepository.Verify(
            r => r.DeleteWhereAsync(It.IsAny<JobExecutionQuery>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _auditRecordRepository.Verify(
            r => r.DeleteWhereAsync(
                It.IsAny<AuditRecordQuery>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _releaseRepository.Verify(
            r => r.DeleteWhereAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _downloadService.Verify(
            r => r.DeleteClearedFailedDownloadsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_jobExecutionCutoff, Is.EqualTo(now.AddDays(-30)).Within(TimeSpan.FromMinutes(1)));
            Assert.That(_auditRecordCutoff, Is.EqualTo(now.AddDays(-90)).Within(TimeSpan.FromMinutes(1)));
            Assert.That(_releaseCutoff, Is.EqualTo(now.AddDays(-7)).Within(TimeSpan.FromMinutes(1)));
            Assert.That(_failedDownloadCutoff, Is.EqualTo(now.AddDays(-30)).Within(TimeSpan.FromMinutes(1)));
        }
    }

    [Test]
    public async Task ExecuteAsync_HonorsParameterOverride_ForJobExecutionRetention()
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
        Assert.That(_jobExecutionCutoff, Is.EqualTo(DateTime.UtcNow.AddDays(-5)).Within(TimeSpan.FromMinutes(1)));
    }

    [Test]
    public async Task ExecuteAsync_HonorsZeroRetentionDays_ClearsRecentRows()
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
        Assert.That(_jobExecutionCutoff, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromMinutes(1)));
    }

    [Test]
    public async Task ExecuteAsync_HonorsParameterOverride_ForFailedDownloadRetention()
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
        Assert.That(_failedDownloadCutoff, Is.EqualTo(DateTime.UtcNow.AddDays(-1)).Within(TimeSpan.FromMinutes(1)));
    }

    [Test]
    public async Task ExecuteAsync_IgnoresZeroFailedDownloadRetentionDays_FallingBackToDefault()
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
        Assert.That(_failedDownloadCutoff, Is.EqualTo(DateTime.UtcNow.AddDays(-30)).Within(TimeSpan.FromMinutes(1)));
    }
}
