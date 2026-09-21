using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs.Downloads;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class DownloadStuckDetectionJobTests
{
    private List<IDownloadClientIntegration> _integrations = null!;
    private Mock<IDownloadRepository> _downloadRepository = null!;
    private Mock<IDownloadService> _downloadService = null!;
    private Mock<IProgressReporter> _progress = null!;
    private FakeTimeProvider _timeProvider = null!;
    private JobContext _jobContext = null!;
    private DateTime _now;
    private IJob _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _now = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(_now, TimeSpan.Zero));
        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        _integrations = [];

        _downloadRepository = new Mock<IDownloadRepository>();
        _downloadRepository
            .Setup(r => r.ListStuckCandidatesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _downloadService = new Mock<IDownloadService>();
        _downloadService
            .Setup(s => s.MarkStuckAsync(
                It.IsAny<StuckDownloadCandidate>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _downloadService
            .Setup(s => s.TryApplyVerifiedClientStateAsync(
                It.IsAny<StuckDownloadCandidate>(),
                It.IsAny<DownloadInfo>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _progress = new Mock<IProgressReporter>();
        _subject = new DownloadStuckDetectionJob(
            _integrations,
            _downloadRepository.Object,
            NullLogger<DownloadStuckDetectionJob>.Instance,
            _timeProvider,
            _downloadService.Object,
            _progress.Object);
    }

    [Test]
    public async Task ExecuteAsync_RequestsCandidates_WhenNoThresholdIsConfigured()
    {
        // Arrange

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadRepository.Verify(
                r => r.ListStuckCandidatesAsync(
                    _now - TimeSpan.FromMinutes(20), _now - TimeSpan.FromMinutes(90), It.IsAny<CancellationToken>()),
                Times.Once);
            _progress.Verify(
                value => value.Report(0, "The detection will get the limits for stuck downloads."),
                Times.Once);
            _progress.Verify(
                value => value.Report(null, "The detection will load the stuck downloads."),
                Times.Once);
            _progress.Verify(value => value.Report(1, "The detection found no stuck downloads."), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_ReportsExactProgress_WhenChangingDownloadStatuses()
    {
        // Arrange
        _downloadRepository
            .Setup(repository => repository.ListStuckCandidatesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new StuckDownloadCandidate
                {
                    DownloadId = Guid.NewGuid(),
                    ExternalId = string.Empty,
                    Title = "first",
                    Status = DownloadStatus.Downloading,
                    InactivityAnchor = _now.AddHours(-1),
                    ProviderType = "test",
                    ClientName = "client",
                    Settings = new Dictionary<string, string>()
                },
                new StuckDownloadCandidate
                {
                    DownloadId = Guid.NewGuid(),
                    ExternalId = string.Empty,
                    Title = "second",
                    Status = DownloadStatus.Downloading,
                    InactivityAnchor = _now.AddHours(-1),
                    ProviderType = "test",
                    ClientName = "client",
                    Settings = new Dictionary<string, string>()
                }
            ]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _progress.Verify(
                value => value.Report(0, "The detection will change the status of 2 stuck downloads."),
                Times.Once);
            _progress.Verify(
                value => value.Report(0.5, "The detection will change the status of 2 stuck downloads."),
                Times.Once);
            _progress.Verify(value => value.Report(1, "The stuck-download detection completed."), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_UsesFetchingThreshold_WhenParameterIsSpecified()
    {
        // Arrange
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["fetchingMinutes"] = "5"
            }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _downloadRepository.Verify(
            r => r.ListStuckCandidatesAsync(
                _now - TimeSpan.FromMinutes(5), _now - TimeSpan.FromMinutes(90), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_UsesStalledThreshold_WhenParameterIsSpecified()
    {
        // Arrange
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["stalledMinutes"] = "30"
            }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _downloadRepository.Verify(
            r => r.ListStuckCandidatesAsync(
                _now - TimeSpan.FromMinutes(20), _now - TimeSpan.FromMinutes(30), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_MarksStuckWithFetchingThreshold_WhenCandidateHasNoExternalId()
    {
        // Arrange
        var candidate = new StuckDownloadCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = string.Empty,
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Downloading,
            InactivityAnchor = new DateTime(2026, 5, 20, 11, 0, 0, DateTimeKind.Utc),
            ProviderType = "stub",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" }
        };
        _downloadRepository
            .Setup(r => r.ListStuckCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);
        var userId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var context = _jobContext with { ManualTriggerUserId = userId, CorrelationId = correlationId };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadService.Verify(
                s => s.MarkStuckAsync(
                    candidate, TimeSpan.FromMinutes(20), _now, userId, correlationId, It.IsAny<CancellationToken>()),
                Times.Once);
            _downloadService.Verify(
                s => s.TryApplyVerifiedClientStateAsync(
                    It.IsAny<StuckDownloadCandidate>(),
                    It.IsAny<DownloadInfo>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_MarksStuckWithStalledThreshold_WhenCandidateIsStalled()
    {
        // Arrange
        var candidate = new StuckDownloadCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = string.Empty,
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Stalled,
            InactivityAnchor = new DateTime(2026, 5, 20, 11, 0, 0, DateTimeKind.Utc),
            ProviderType = "stub",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" }
        };
        _downloadRepository
            .Setup(r => r.ListStuckCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _downloadService.Verify(
            s => s.MarkStuckAsync(
                candidate, TimeSpan.FromMinutes(90), _now, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_MarksStuck_WhenNoIntegrationRegisteredForProviderType()
    {
        // Arrange
        var candidate = new StuckDownloadCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = string.Empty,
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Downloading,
            InactivityAnchor = new DateTime(2026, 5, 20, 11, 0, 0, DateTimeKind.Utc),
            ProviderType = "stub",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" }
        };
        _downloadRepository
            .Setup(r => r.ListStuckCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _downloadService.Verify(
            s => s.MarkStuckAsync(
                candidate, TimeSpan.FromMinutes(20), _now, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_RefreshesAndSkipsMarkStuck_WhenClientStateApplied()
    {
        // Arrange
        var externalId = Guid.NewGuid().ToString("N");
        var candidate = new StuckDownloadCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = externalId,
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Downloading,
            InactivityAnchor = new DateTime(2026, 5, 20, 11, 0, 0, DateTimeKind.Utc),
            ProviderType = "stub",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" }
        };
        _downloadRepository
            .Setup(r => r.ListStuckCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);
        var info = new DownloadInfo { ExternalId = externalId, Status = DownloadStatus.Downloading };
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("stub");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo> { [externalId] = info },
                new Dictionary<string, Exception>()));
        _integrations.Add(integration.Object);
        _downloadService
            .Setup(s => s.TryApplyVerifiedClientStateAsync(
                It.IsAny<StuckDownloadCandidate>(),
                It.IsAny<DownloadInfo>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadService.Verify(
                s => s.TryApplyVerifiedClientStateAsync(
                    candidate,
                    info,
                    _now - TimeSpan.FromMinutes(90),
                    _now,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _downloadService.Verify(
                s => s.MarkStuckAsync(
                    It.IsAny<StuckDownloadCandidate>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            integration.Verify(
                i => i.GetDownloadsAsync(
                    It.Is<IEnumerable<string>>(ids => ids.Single() == externalId),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_MarksStuck_WhenClientStateNotApplied()
    {
        // Arrange
        var externalId = Guid.NewGuid().ToString("N");
        var candidate = new StuckDownloadCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = externalId,
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Downloading,
            InactivityAnchor = new DateTime(2026, 5, 20, 11, 0, 0, DateTimeKind.Utc),
            ProviderType = "stub",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" }
        };
        _downloadRepository
            .Setup(r => r.ListStuckCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("stub");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>
                {
                    [externalId] = new DownloadInfo { ExternalId = externalId, Status = DownloadStatus.Downloading }
                },
                new Dictionary<string, Exception>()));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _downloadService.Verify(
            s => s.MarkStuckAsync(
                candidate, TimeSpan.FromMinutes(20), _now, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SkipsCandidate_WhenClientVerificationThrows()
    {
        // Arrange
        var externalId = Guid.NewGuid().ToString("N");
        var candidate = new StuckDownloadCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = externalId,
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Downloading,
            InactivityAnchor = new DateTime(2026, 5, 20, 11, 0, 0, DateTimeKind.Utc),
            ProviderType = "stub",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" }
        };
        _downloadRepository
            .Setup(r => r.ListStuckCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("stub");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("client timed out"));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadService.Verify(
                s => s.MarkStuckAsync(
                    It.IsAny<StuckDownloadCandidate>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _downloadService.Verify(
                s => s.TryApplyVerifiedClientStateAsync(
                    It.IsAny<StuckDownloadCandidate>(),
                    It.IsAny<DownloadInfo>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsCandidate_WhenClientSettingsCannotBeRead()
    {
        // Arrange
        var candidate = new StuckDownloadCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Downloading,
            InactivityAnchor = new DateTime(2026, 5, 20, 11, 0, 0, DateTimeKind.Utc),
            ProviderType = "stub",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = null
        };
        _downloadRepository
            .Setup(r => r.ListStuckCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("stub");
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            integration.Verify(
                i => i.GetDownloadsAsync(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _downloadService.Verify(
                s => s.MarkStuckAsync(
                    It.IsAny<StuckDownloadCandidate>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsCandidate_WhenBatchReportsFailureForExternalId()
    {
        // Arrange
        var externalId = Guid.NewGuid().ToString("N");
        var candidate = new StuckDownloadCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = externalId,
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Downloading,
            InactivityAnchor = new DateTime(2026, 5, 20, 11, 0, 0, DateTimeKind.Utc),
            ProviderType = "stub",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" }
        };
        _downloadRepository
            .Setup(r => r.ListStuckCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("stub");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(),
                new Dictionary<string, Exception> { [externalId] = new TimeoutException("per-item failure") }));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadService.Verify(
                s => s.MarkStuckAsync(
                    It.IsAny<StuckDownloadCandidate>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _downloadService.Verify(
                s => s.TryApplyVerifiedClientStateAsync(
                    It.IsAny<StuckDownloadCandidate>(),
                    It.IsAny<DownloadInfo>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_MarksStuck_WhenClientReportsNoStatusForExternalId()
    {
        // Arrange
        var externalId = Guid.NewGuid().ToString("N");
        var candidate = new StuckDownloadCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = externalId,
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Downloading,
            InactivityAnchor = new DateTime(2026, 5, 20, 11, 0, 0, DateTimeKind.Utc),
            ProviderType = "stub",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" }
        };
        _downloadRepository
            .Setup(r => r.ListStuckCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("stub");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(),
                new Dictionary<string, Exception>()));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _downloadService.Verify(
            s => s.MarkStuckAsync(
                candidate, TimeSpan.FromMinutes(20), _now, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ContinuesToNextCandidate_WhenMarkStuckThrows()
    {
        // Arrange
        var first = new StuckDownloadCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = string.Empty,
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Downloading,
            InactivityAnchor = new DateTime(2026, 5, 20, 11, 0, 0, DateTimeKind.Utc),
            ProviderType = "stub",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" }
        };
        var second = new StuckDownloadCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = string.Empty,
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Downloading,
            InactivityAnchor = new DateTime(2026, 5, 20, 11, 0, 0, DateTimeKind.Utc),
            ProviderType = "stub",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" }
        };
        _downloadRepository
            .Setup(r => r.ListStuckCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        _downloadService
            .Setup(s => s.MarkStuckAsync(
                first,
                It.IsAny<TimeSpan>(),
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("write raced"));

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _downloadService.Verify(
            s => s.MarkStuckAsync(
                second, TimeSpan.FromMinutes(20), _now, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
