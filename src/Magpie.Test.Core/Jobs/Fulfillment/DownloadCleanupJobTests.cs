using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core.Jobs.Downloads;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class DownloadCleanupJobTests
{
    private Mock<IDownloadClientIntegration> _torrentIntegration = null!;
    private Mock<IDownloadClientIntegration> _usenetIntegration = null!;
    private Mock<IDownloadRepository> _downloadRepository = null!;
    private Mock<IDataProtectionService> _protector = null!;
    private Mock<IProgressReporter> _progress = null!;
    private FakeTimeProvider _timeProvider = null!;
    private IJob _subject = null!;
    private JobContext _jobContext = null!;
    private DateTime _now;

    [SetUp]
    public void SetUp()
    {
        _now = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(_now, TimeSpan.Zero));

        _torrentIntegration = new Mock<IDownloadClientIntegration>();
        _torrentIntegration.SetupGet(i => i.ProviderType).Returns("torrent-client");
        _torrentIntegration
            .Setup(i => i.RemoveDownloadAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _usenetIntegration = new Mock<IDownloadClientIntegration>();
        _usenetIntegration.SetupGet(i => i.ProviderType).Returns("usenet-client");
        _usenetIntegration
            .Setup(i => i.RemoveDownloadAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _protector = new Mock<IDataProtectionService>();
        _protector
            .Setup(p => p.Unprotect(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns((IReadOnlyDictionary<string, string> stored) => new Dictionary<string, string>(stored));

        _downloadRepository = new Mock<IDownloadRepository>();
        _downloadRepository
            .Setup(r => r.ListCleanupCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _downloadRepository
            .Setup(r => r.StampClientRemovedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _progress = new Mock<IProgressReporter>();
        _subject = new DownloadCleanupJob(
            [_torrentIntegration.Object, _usenetIntegration.Object],
            _downloadRepository.Object,
            _protector.Object,
            NullLogger<DownloadCleanupJob>.Instance,
            _timeProvider,
            _progress.Object);

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
    public async Task ExecuteAsync_ReportsTerminalSkip_WhenNoDownloadMatches()
    {
        // Arrange

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _progress.Verify(
                value => value.Report(null, "The cleanup will load matching downloads."),
                Times.Once);
            _progress.Verify(value => value.Report(1, "The cleanup stopped because no download matched."), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_ReportsExactProgress_WhenEvaluatingDownloads()
    {
        // Arrange
        _downloadRepository
            .Setup(repository => repository.ListCleanupCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new DownloadCleanupCandidate
                {
                    DownloadId = Guid.NewGuid(),
                    ExternalId = string.Empty,
                    Title = "first",
                    Status = DownloadStatus.Failed,
                    HeldByPolicy = false,
                    ProviderType = "torrent-client",
                    ClientName = "client",
                    Settings = new Dictionary<string, string>(),
                    CleanupDeleteFiles = false,
                    KeepFailedOnClient = false
                },
                new DownloadCleanupCandidate
                {
                    DownloadId = Guid.NewGuid(),
                    ExternalId = string.Empty,
                    Title = "second",
                    Status = DownloadStatus.Failed,
                    HeldByPolicy = false,
                    ProviderType = "torrent-client",
                    ClientName = "client",
                    Settings = new Dictionary<string, string>(),
                    CleanupDeleteFiles = false,
                    KeepFailedOnClient = false
                }
            ]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _progress.Verify(
                value => value.Report(0, "The cleanup will evaluate 2 downloads."),
                Times.Once);
            _progress.Verify(
                value => value.Report(0.5, "The cleanup will evaluate 2 downloads."),
                Times.Once);
            _progress.Verify(value => value.Report(1, "The download cleanup completed."), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_RemovesFailedRow_AndHonorsDeleteFilesDefault()
    {
        // Arrange
        var candidate = new DownloadCleanupCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Failed,
            HeldByPolicy = false,
            ProviderType = "torrent-client",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CleanupDeleteFiles = true,
            KeepFailedOnClient = false
        };
        _downloadRepository
            .Setup(r => r.ListCleanupCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _torrentIntegration.Verify(
                i => i.RemoveDownloadAsync(
                    candidate.ExternalId,
                    true,
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _torrentIntegration.Verify(
                i => i.RemoveDownloadAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _downloadRepository.Verify(
                r => r.StampClientRemovedAsync(
                    candidate.DownloadId,
                    candidate.Title,
                    candidate.ClientName,
                    "failed",
                    _now,
                    null,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_PassesDeleteFilesFalse_WhenSettingOff()
    {
        // Arrange
        var candidate = new DownloadCleanupCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Failed,
            HeldByPolicy = false,
            ProviderType = "torrent-client",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CleanupDeleteFiles = false,
            KeepFailedOnClient = false
        };
        _downloadRepository
            .Setup(r => r.ListCleanupCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _torrentIntegration.Verify(
                i => i.RemoveDownloadAsync(
                    It.IsAny<string>(),
                    false,
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _torrentIntegration.Verify(
                i => i.RemoveDownloadAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_LeavesFailedRow_WhenKeepFailedEscapeHatchOn()
    {
        // Arrange
        var candidate = new DownloadCleanupCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Failed,
            HeldByPolicy = false,
            ProviderType = "torrent-client",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CleanupDeleteFiles = true,
            KeepFailedOnClient = true
        };
        _downloadRepository
            .Setup(r => r.ListCleanupCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _torrentIntegration.Verify(
                i => i.RemoveDownloadAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _downloadRepository.Verify(
                r => r.StampClientRemovedAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_AppliesEachClientsOwnCleanupSettings_Independently()
    {
        // Arrange
        var keepCandidate = new DownloadCleanupCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Failed,
            HeldByPolicy = false,
            ProviderType = "torrent-client",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CleanupDeleteFiles = true,
            KeepFailedOnClient = true
        };
        var removeCandidate = new DownloadCleanupCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Failed,
            HeldByPolicy = false,
            ProviderType = "torrent-client",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CleanupDeleteFiles = true,
            KeepFailedOnClient = false
        };
        _downloadRepository
            .Setup(r => r.ListCleanupCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([keepCandidate, removeCandidate]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadRepository.Verify(
                r => r.StampClientRemovedAsync(
                    keepCandidate.DownloadId,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _downloadRepository.Verify(
                r => r.StampClientRemovedAsync(
                    removeCandidate.DownloadId,
                    removeCandidate.Title,
                    removeCandidate.ClientName,
                    "failed",
                    _now,
                    null,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _torrentIntegration.Verify(
                i => i.RemoveDownloadAsync(
                    removeCandidate.ExternalId,
                    It.IsAny<bool>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _torrentIntegration.Verify(
                i => i.RemoveDownloadAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_RemovesImportedUsenetRow_Immediately()
    {
        // Arrange
        var candidate = new DownloadCleanupCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Imported,
            HeldByPolicy = false,
            ProviderType = "usenet-client",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CleanupDeleteFiles = true,
            KeepFailedOnClient = false
        };
        _downloadRepository
            .Setup(r => r.ListCleanupCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _usenetIntegration.Verify(
                i => i.RemoveDownloadAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _downloadRepository.Verify(
                r => r.StampClientRemovedAsync(
                    candidate.DownloadId,
                    candidate.Title,
                    candidate.ClientName,
                    "client-released",
                    _now,
                    null,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_WaitsOnImportedDownload_UntilClientReleasesIt()
    {
        // Arrange
        var candidate = new DownloadCleanupCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Imported,
            HeldByPolicy = true,
            ProviderType = "torrent-client",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CleanupDeleteFiles = true,
            KeepFailedOnClient = false
        };
        _downloadRepository
            .Setup(r => r.ListCleanupCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _downloadRepository.Verify(
            r => r.StampClientRemovedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        var released = candidate with { HeldByPolicy = false };
        _downloadRepository
            .Setup(r => r.ListCleanupCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([released]);

        await _subject.ExecuteAsync(_jobContext with { ExecutionId = Guid.NewGuid() }).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            _downloadRepository.Verify(
                r => r.StampClientRemovedAsync(
                    released.DownloadId,
                    released.Title,
                    released.ClientName,
                    "client-released",
                    _now,
                    null,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _torrentIntegration.Verify(
                i => i.RemoveDownloadAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsImportedHeldDownload_WithoutExternalId()
    {
        // Arrange
        var candidate = new DownloadCleanupCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = string.Empty,
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Imported,
            HeldByPolicy = true,
            ProviderType = "torrent-client",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CleanupDeleteFiles = true,
            KeepFailedOnClient = false
        };
        _downloadRepository
            .Setup(r => r.ListCleanupCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _torrentIntegration.Verify(
                i => i.RemoveDownloadAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _downloadRepository.Verify(
                r => r.StampClientRemovedAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_RemovesImportedDownload_WhenHonorClientHoldPolicyOff()
    {
        // Arrange
        var candidate = new DownloadCleanupCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Imported,
            HeldByPolicy = true,
            ProviderType = "torrent-client",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string>
            {
                ["host"] = "localhost",
                [DownloadClientSettingsKeys.HonorClientHoldPolicy] = "false"
            },
            CleanupDeleteFiles = true,
            KeepFailedOnClient = false
        };
        _downloadRepository
            .Setup(r => r.ListCleanupCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _downloadRepository.Verify(
            r => r.StampClientRemovedAsync(
                candidate.DownloadId,
                candidate.Title,
                candidate.ClientName,
                "hold-policy-ignored",
                _now,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_LeavesRowUnstampedAndRetriesNextCleanup_WhenClientFails()
    {
        // Arrange
        var candidate = new DownloadCleanupCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Failed,
            HeldByPolicy = false,
            ProviderType = "torrent-client",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CleanupDeleteFiles = true,
            KeepFailedOnClient = false
        };
        _downloadRepository
            .Setup(r => r.ListCleanupCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);
        var removeResult = false;
        _torrentIntegration
            .Setup(i => i.RemoveDownloadAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(removeResult));

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _downloadRepository.Verify(
            r => r.StampClientRemovedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        removeResult = true;

        await _subject.ExecuteAsync(_jobContext with { ExecutionId = Guid.NewGuid() }).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            _downloadRepository.Verify(
                r => r.StampClientRemovedAsync(
                    candidate.DownloadId,
                    candidate.Title,
                    candidate.ClientName,
                    "failed",
                    _now,
                    null,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _torrentIntegration.Verify(
                i => i.RemoveDownloadAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
    }

    [Test]
    public async Task ExecuteAsync_LeavesRowUnstamped_WhenClientThrows()
    {
        // Arrange
        var candidate = new DownloadCleanupCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Failed,
            HeldByPolicy = false,
            ProviderType = "torrent-client",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CleanupDeleteFiles = true,
            KeepFailedOnClient = false
        };
        _downloadRepository
            .Setup(r => r.ListCleanupCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);
        _torrentIntegration
            .Setup(i => i.RemoveDownloadAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Remove failed"));

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _downloadRepository.Verify(
            r => r.StampClientRemovedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotDeleteDownloadRow_WhenCleanupCompletes()
    {
        // Arrange
        var candidate = new DownloadCleanupCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Failed,
            HeldByPolicy = false,
            ProviderType = "torrent-client",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CleanupDeleteFiles = true,
            KeepFailedOnClient = false
        };
        _downloadRepository
            .Setup(r => r.ListCleanupCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadRepository.Verify(
                r => r.StampClientRemovedAsync(
                    candidate.DownloadId,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _downloadRepository.Verify(
                r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_PassesUserAndCorrelation_ToRemovalStamp()
    {
        // Arrange
        var candidate = new DownloadCleanupCandidate
        {
            DownloadId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = $"download-{Guid.NewGuid():N}",
            Status = DownloadStatus.Failed,
            HeldByPolicy = false,
            ProviderType = "torrent-client",
            ClientName = $"client-{Guid.NewGuid():N}",
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CleanupDeleteFiles = true,
            KeepFailedOnClient = false
        };
        _downloadRepository
            .Setup(r => r.ListCleanupCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidate]);
        var userId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var context = _jobContext with
        {
            ManualTriggerUserId = userId,
            CorrelationId = correlationId
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _downloadRepository.Verify(
            r => r.StampClientRemovedAsync(
                candidate.DownloadId,
                candidate.Title,
                candidate.ClientName,
                "failed",
                _now,
                userId,
                correlationId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
