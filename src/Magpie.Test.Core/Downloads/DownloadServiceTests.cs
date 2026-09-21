using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Downloads;

[TestFixture]
public sealed class DownloadServiceTests
{
    private Mock<IDownloadRepository> _downloadRepository = null!;
    private Mock<IDownloadClientIntegration> _integration = null!;
    private Mock<IJobManager> _jobManager = null!;
    private StuckDownloadCandidate _candidate = null!;
    private DownloadInfo _downloadedInfo = null!;
    private DateTime _now;
    private Guid? _refreshedDownloadId;
    private DownloadStatus? _refreshedStatus;
    private IDownloadService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _downloadRepository = new Mock<IDownloadRepository> { CallBase = true };
        _downloadRepository
            .Setup(r => r.ListFailedByReleaseIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<ReleaseIdentityKey>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _integration = new Mock<IDownloadClientIntegration>();
        _integration.SetupGet(i => i.ProviderType).Returns("fake");
        _integration
            .Setup(i => i.DownloadReleaseAsync(
                It.IsAny<Release>(),
                It.IsAny<ReleaseGrabContext>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadInfo { ExternalId = "default", Status = DownloadStatus.Queued });

        _refreshedDownloadId = null;
        _refreshedStatus = null;
        _downloadRepository
            .Setup(repository => repository.UpdateActiveRefreshedStatusAsync(
                It.IsAny<Guid>(),
                It.IsAny<DownloadStatus>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<decimal?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<bool>(),
                It.IsAny<DownloadFailureReason?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback((
                Guid downloadId,
                DownloadStatus status,
                string? _,
                string? _,
                DateTime? _,
                decimal? _,
                DateTime? _,
                bool _,
                DownloadFailureReason? _,
                DateTime? _,
                DateTime _,
                Guid? _,
                CancellationToken _) =>
            {
                _refreshedDownloadId = downloadId;
                _refreshedStatus = status;
            })
            .ReturnsAsync(true);
        _jobManager = new Mock<IJobManager>();
        _jobManager
            .Setup(manager => manager.ExecuteAsync(
                It.IsAny<JobEnqueueRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.Empty);
        _now = new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc);
        _candidate = new StuckDownloadCandidate
        {
            DownloadId = new Guid("65bf4f33-77dc-4ae7-a4b9-9d89012df7b6"),
            ExternalId = "external-download",
            Title = "Download",
            Status = DownloadStatus.Downloading,
            InactivityAnchor = new DateTime(2026, 8, 5, 11, 0, 0, DateTimeKind.Utc),
            ProviderType = "fake",
            ClientName = "Client",
            Settings = new Dictionary<string, string>()
        };
        _downloadedInfo = new DownloadInfo
        {
            ExternalId = "external-download",
            Status = DownloadStatus.Downloaded
        };
        _subject = new DownloadService(
            [],
            Mock.Of<IDownloadClientRepository>(),
            _downloadRepository.Object,
            _jobManager.Object,
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);
    }

    [Test]
    public async Task GrabAsync_ReturnsDownloadInfo_WhenTheIntegrationSucceeds()
    {
        // Arrange
        _integration
            .Setup(i => i.DownloadReleaseAsync(
                It.IsAny<Release>(),
                It.IsAny<ReleaseGrabContext>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadInfo
            {
                ExternalId = "ext-1",
                Status = DownloadStatus.Queued,
                StatusReason = "queued",
                OutputPath = "/downloads/one"
            });
        var service = new DownloadService(
            [_integration.Object],
            Mock.Of<IDownloadClientRepository>(),
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Sample release",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ObservedAt = DateTime.UtcNow
        };
        release.IdentityKeys.Add(new ReleaseIdentityKey
        {
            Authority = "test.release",
            Value = release.ExternalId,
            Ordinal = 0
        });
        var source = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Test client",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var result = await service
            .GrabAsync(release, source, source.Settings, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.AlreadyTainted, Is.False);
            Assert.That(result.ClientUnreachable, Is.False);
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(result.DownloadInfo, Is.Not.Null);
            Assert.That(result.DownloadInfo!.ExternalId, Is.EqualTo("ext-1"));
            _integration.Verify(
                i => i.DownloadReleaseAsync(
                    It.Is<Release>(r => ReferenceEquals(r, release)),
                    It.IsAny<ReleaseGrabContext>(),
                    source.Settings,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task GrabAsync_ForwardsContentType_WhenCallingIntegration()
    {
        // Arrange
        var service = new DownloadService(
            [_integration.Object],
            Mock.Of<IDownloadClientRepository>(),
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Sample release",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ObservedAt = DateTime.UtcNow
        };
        release.IdentityKeys.Add(new ReleaseIdentityKey
        {
            Authority = "test.release",
            Value = release.ExternalId,
            Ordinal = 0
        });
        var source = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Test client",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await service
            .GrabAsync(release, source, source.Settings, contentType: "type-a", cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        _integration.Verify(
            i => i.DownloadReleaseAsync(
                It.IsAny<Release>(),
                It.Is<ReleaseGrabContext>(c => c.ContentType == "type-a"),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void GrabAsync_Throws_WhenNoIntegrationMatchesProviderType()
    {
        // Arrange
        var service = new DownloadService(
            [_integration.Object],
            Mock.Of<IDownloadClientRepository>(),
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Sample release",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ObservedAt = DateTime.UtcNow
        };
        release.IdentityKeys.Add(new ReleaseIdentityKey
        {
            Authority = "test.release",
            Value = release.ExternalId,
            Ordinal = 0
        });
        var source = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Test client",
            ProviderType = "unregistered",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CreatedAt = DateTime.UtcNow
        };

        // Act
        async Task Act() => await service
            .GrabAsync(release, source, source.Settings, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(Act);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex!.Message, Does.Contain("unregistered"));
            _integration.Verify(
                i => i.DownloadReleaseAsync(
                    It.IsAny<Release>(),
                    It.IsAny<ReleaseGrabContext>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public void GrabAsync_Throws_WhenSourceProviderTypeIsBlank()
    {
        // Arrange
        var service = new DownloadService(
            [_integration.Object],
            Mock.Of<IDownloadClientRepository>(),
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Sample release",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ObservedAt = DateTime.UtcNow
        };
        release.IdentityKeys.Add(new ReleaseIdentityKey
        {
            Authority = "test.release",
            Value = release.ExternalId,
            Ordinal = 0
        });
        var source = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Test client",
            ProviderType = "   ",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CreatedAt = DateTime.UtcNow
        };

        // Act
        async Task Act() => await service
            .GrabAsync(release, source, source.Settings, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        Assert.ThrowsAsync<InvalidOperationException>(Act);
        _integration.Verify(
            i => i.DownloadReleaseAsync(
                It.IsAny<Release>(),
                It.IsAny<ReleaseGrabContext>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task GrabAsync_PicksLastRegisteredIntegration_WhenMultipleClaimSameProviderType()
    {
        // Arrange
        var older = new Mock<IDownloadClientIntegration>();
        older.SetupGet(i => i.ProviderType).Returns("fake");
        older
            .Setup(i => i.DownloadReleaseAsync(
                It.IsAny<Release>(),
                It.IsAny<ReleaseGrabContext>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadInfo { ExternalId = "older", Status = DownloadStatus.Queued });
        var newer = new Mock<IDownloadClientIntegration>();
        newer.SetupGet(i => i.ProviderType).Returns("fake");
        newer
            .Setup(i => i.DownloadReleaseAsync(
                It.IsAny<Release>(),
                It.IsAny<ReleaseGrabContext>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadInfo { ExternalId = "newer", Status = DownloadStatus.Queued });
        var service = new DownloadService(
            [older.Object, newer.Object],
            Mock.Of<IDownloadClientRepository>(),
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Sample release",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ObservedAt = DateTime.UtcNow
        };
        release.IdentityKeys.Add(new ReleaseIdentityKey
        {
            Authority = "test.release",
            Value = release.ExternalId,
            Ordinal = 0
        });
        var source = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Test client",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var result = await service
            .GrabAsync(release, source, source.Settings, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.DownloadInfo!.ExternalId, Is.EqualTo("newer"));
            older.Verify(
                i => i.DownloadReleaseAsync(
                    It.IsAny<Release>(),
                    It.IsAny<ReleaseGrabContext>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            newer.Verify(
                i => i.DownloadReleaseAsync(
                    It.IsAny<Release>(),
                    It.IsAny<ReleaseGrabContext>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public void GrabAsync_PropagatesException_WhenIntegrationFails()
    {
        // Arrange
        _integration
            .Setup(i => i.DownloadReleaseAsync(
                It.IsAny<Release>(),
                It.IsAny<ReleaseGrabContext>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("client refused"));
        var service = new DownloadService(
            [_integration.Object],
            Mock.Of<IDownloadClientRepository>(),
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Sample release",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ObservedAt = DateTime.UtcNow
        };
        release.IdentityKeys.Add(new ReleaseIdentityKey
        {
            Authority = "test.release",
            Value = release.ExternalId,
            Ordinal = 0
        });
        var source = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Test client",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CreatedAt = DateTime.UtcNow
        };

        // Act
        async Task Act() => await service
            .GrabAsync(release, source, source.Settings, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(Act);
        Assert.That(ex!.Message, Is.EqualTo("client refused"));
    }

    [Test]
    public async Task GrabAsync_ReturnsAlreadyTainted_WhenRetainedFailedIdentityMatches()
    {
        // Arrange
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Sample release",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ObservedAt = DateTime.UtcNow
        };
        release.IdentityKeys.Add(new ReleaseIdentityKey
        {
            Authority = "test.release",
            Value = release.ExternalId,
            Ordinal = 0
        });
        var source = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Test client",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CreatedAt = DateTime.UtcNow
        };
        _downloadRepository
            .Setup(r => r.ListFailedByReleaseIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<ReleaseIdentityKey>>(),
                source.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Download
                {
                    Id = Guid.NewGuid(),
                    ReleaseId = release.Id,
                    DownloadClientId = source.Id,
                    ExternalId = "ext-failed",
                    Title = "Failed row",
                    Status = DownloadStatus.Failed,
                    FailedAt = DateTime.UtcNow.AddDays(-1),
                    CreatedAt = DateTime.UtcNow.AddDays(-1)
                }
            ]);
        var service = new DownloadService(
            [_integration.Object],
            Mock.Of<IDownloadClientRepository>(),
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);

        // Act
        var result = await service
            .GrabAsync(release, source, source.Settings, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.AlreadyTainted, Is.True);
            Assert.That(result.ClientUnreachable, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Release tainted by prior Failed grab"));
            Assert.That(result.DownloadInfo, Is.Null);
            _downloadRepository.Verify(
                r => r.ListFailedByReleaseIdentitiesAsync(
                    It.Is<IReadOnlyCollection<ReleaseIdentityKey>>(k =>
                        k.Count == 1 && k.Single().Value == release.ExternalId),
                    source.Id,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _integration.Verify(
                i => i.DownloadReleaseAsync(
                    It.IsAny<Release>(),
                    It.IsAny<ReleaseGrabContext>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task GrabAsync_Proceeds_WhenFailedIdentityMatchesAndOverrideRequested()
    {
        // Arrange
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Sample release",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ObservedAt = DateTime.UtcNow
        };
        release.IdentityKeys.Add(new ReleaseIdentityKey
        {
            Authority = "test.release",
            Value = release.ExternalId,
            Ordinal = 0
        });
        var source = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Test client",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = new Dictionary<string, string> { ["host"] = "localhost" },
            CreatedAt = DateTime.UtcNow
        };
        _downloadRepository
            .Setup(r => r.ListFailedByReleaseIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<ReleaseIdentityKey>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Download
                {
                    Id = Guid.NewGuid(),
                    ReleaseId = release.Id,
                    DownloadClientId = source.Id,
                    ExternalId = "ext-failed",
                    Title = "Failed row",
                    Status = DownloadStatus.Failed,
                    FailedAt = DateTime.UtcNow.AddDays(-1),
                    CreatedAt = DateTime.UtcNow.AddDays(-1)
                }
            ]);
        _integration
            .Setup(i => i.DownloadReleaseAsync(
                It.IsAny<Release>(),
                It.IsAny<ReleaseGrabContext>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadInfo { ExternalId = "ext-manual", Status = DownloadStatus.Queued });
        var service = new DownloadService(
            [_integration.Object],
            Mock.Of<IDownloadClientRepository>(),
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);

        // Act
        var result = await service
            .GrabAsync(release, source, source.Settings, ignoreFailedTaint: true, cancellationToken: default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.AlreadyTainted, Is.False);
            Assert.That(result.DownloadInfo!.ExternalId, Is.EqualTo("ext-manual"));
            _downloadRepository.Verify(
                r => r.ListFailedByReleaseIdentitiesAsync(
                    It.IsAny<IReadOnlyCollection<ReleaseIdentityKey>>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _integration.Verify(
                i => i.DownloadReleaseAsync(
                    It.IsAny<Release>(),
                    It.IsAny<ReleaseGrabContext>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task PauseAsync_DispatchesWithRepositorySettings_WhenClientHasSecretFields()
    {
        // Arrange
        var downloadId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var clientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var settings = new Dictionary<string, string>
        {
            ["host"] = "127.0.0.1",
            ["port"] = "8080",
            ["apiKey"] = "plain-secret"
        };
        var client = new DownloadClient
        {
            Id = clientId,
            Name = "Mock client",
            ProviderType = "pausable-fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = settings,
            CreatedAt = DateTime.UtcNow
        };
        var download = new Download
        {
            Id = downloadId,
            ReleaseId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            DownloadClientId = clientId,
            ExternalId = "ext-paused",
            Title = "Queued item",
            Status = DownloadStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };
        var clientRepository = new Mock<IDownloadClientRepository> { CallBase = true };
        clientRepository
            .Setup(r => r.GetByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _downloadRepository
            .Setup(r => r.GetByIdAsync(downloadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(download);
        _downloadRepository
            .Setup(r => r.PatchAsync(It.IsAny<DownloadPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var pausable = new Mock<IDownloadClientIntegration>();
        pausable.SetupGet(i => i.ProviderType).Returns("pausable-fake");
        var pausableCapability = pausable.As<IPausableDownloadClient>();
        pausableCapability
            .Setup(c => c.PauseDownloadAsync("ext-paused", settings, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = new DownloadService(
            [pausable.Object],
            clientRepository.Object,
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);

        // Act
        var accepted = await service.PauseAsync(downloadId, CancellationToken.None).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(accepted, Is.True);
            pausableCapability.Verify(
                c => c.PauseDownloadAsync(
                    "ext-paused",
                    It.Is<IReadOnlyDictionary<string, string>>(s =>
                        s["host"] == "127.0.0.1" &&
                        s["port"] == "8080" &&
                        s["apiKey"] == "plain-secret"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _downloadRepository.Verify(
                r => r.PatchAsync(
                    It.Is<DownloadPatch>(p =>
                        p.Id == downloadId &&
                        p.Status != null &&
                        p.Status.Value == DownloadStatus.Paused &&
                        p.StatusReason != null &&
                        p.StatusReason.Value == "Operator pause"),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task BulkPauseAsync_DispatchesWithRepositorySettings_WhenClientHasSecretFields()
    {
        // Arrange
        var downloadId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var clientId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var settings = new Dictionary<string, string>
        {
            ["host"] = "127.0.0.1",
            ["port"] = "8080",
            ["apiKey"] = "bulk-secret"
        };
        var client = new DownloadClient
        {
            Id = clientId,
            Name = "Mock client",
            ProviderType = "pausable-fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = settings,
            CreatedAt = DateTime.UtcNow
        };
        var download = new Download
        {
            Id = downloadId,
            ReleaseId = Guid.Parse("66666666-6666-6666-6666-666666666666"),
            DownloadClientId = clientId,
            ExternalId = "ext-bulk",
            Title = "Queued item",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        var clientRepository = new Mock<IDownloadClientRepository> { CallBase = true };
        clientRepository
            .Setup(r => r.GetByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _downloadRepository
            .Setup(r => r.GetByIdAsync(downloadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(download);
        _downloadRepository
            .Setup(r => r.BulkPatchAsync(
                It.IsAny<BulkPatch<DownloadQuery, DownloadPatch>>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkResult { Count = 1 });
        var pausable = new Mock<IDownloadClientIntegration>();
        pausable.SetupGet(i => i.ProviderType).Returns("pausable-fake");
        var pausableCapability = pausable.As<IPausableDownloadClient>();
        pausableCapability
            .Setup(c => c.PauseDownloadAsync("ext-bulk", settings, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = new DownloadService(
            [pausable.Object],
            clientRepository.Object,
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);

        // Act
        var result = await service
            .BulkPauseAsync(new BulkAction<DownloadQuery> { Ids = [downloadId] }, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(1));
            pausableCapability.Verify(
                c => c.PauseDownloadAsync(
                    "ext-bulk",
                    It.Is<IReadOnlyDictionary<string, string>>(s =>
                        s["host"] == "127.0.0.1" &&
                        s["port"] == "8080" &&
                        s["apiKey"] == "bulk-secret"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task BulkPauseAsync_PatchesNoDownload_WhenTheClientRefusesThePause()
    {
        // Arrange
        var downloadId = Guid.Parse("44444444-4444-4444-4444-444444444445");
        var clientId = Guid.Parse("55555555-5555-5555-5555-555555555554");
        var client = new DownloadClient
        {
            Id = clientId,
            Name = "Mock client",
            ProviderType = "pausable-fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = new Dictionary<string, string> { ["host"] = "127.0.0.1" },
            CreatedAt = DateTime.UtcNow
        };
        var download = new Download
        {
            Id = downloadId,
            ReleaseId = Guid.Parse("66666666-6666-6666-6666-666666666667"),
            DownloadClientId = clientId,
            ExternalId = "ext-refused",
            Title = "Queued item",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        var clientRepository = new Mock<IDownloadClientRepository> { CallBase = true };
        clientRepository
            .Setup(r => r.GetByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _downloadRepository
            .Setup(r => r.GetByIdAsync(downloadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(download);
        _downloadRepository
            .Setup(r => r.BulkPatchAsync(
                It.IsAny<BulkPatch<DownloadQuery, DownloadPatch>>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkResult { Count = 0 });
        var pausable = new Mock<IDownloadClientIntegration>();
        pausable.SetupGet(i => i.ProviderType).Returns("pausable-fake");
        pausable.As<IPausableDownloadClient>()
            .Setup(c => c.PauseDownloadAsync(
                "ext-refused",
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = new DownloadService(
            [pausable.Object],
            clientRepository.Object,
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);

        // Act
        var result = await service
            .BulkPauseAsync(new BulkAction<DownloadQuery> { Ids = [downloadId] }, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(0));
            _downloadRepository.Verify(
                r => r.BulkPatchAsync(
                    It.Is<BulkPatch<DownloadQuery, DownloadPatch>>(patch => patch.Ids!.Count == 0),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ApplyRefreshedClientStateAsync_StampsLastProgressAt_WhenTheDownloadCompletes()
    {
        // Arrange
        var downloadId = Guid.Parse("77777777-7777-7777-7777-777777777771");
        var download = new Download
        {
            Id = downloadId,
            ReleaseId = Guid.Parse("77777777-7777-7777-7777-777777777772"),
            DownloadClientId = Guid.Parse("77777777-7777-7777-7777-777777777773"),
            ExternalId = "ext-complete",
            Title = "Finishing item",
            Status = DownloadStatus.Downloading,
            Progress = 100m,
            LastProgressAt = new DateTime(2026, 8, 5, 11, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        var info = new DownloadInfo
        {
            ExternalId = "ext-complete",
            Status = DownloadStatus.Downloaded,
            ProgressPercent = 100m
        };
        DateTime? observedLastProgressAt = null;
        _downloadRepository
            .Setup(r => r.UpdateRefreshedStatusAsync(
                It.IsAny<Guid>(),
                It.IsAny<DownloadStatus>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<decimal?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<bool>(),
                It.IsAny<DownloadFailureReason?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback((
                Guid _,
                DownloadStatus _,
                string? _,
                string? _,
                DateTime? _,
                decimal? _,
                DateTime? lastProgressAt,
                bool _,
                DownloadFailureReason? _,
                DateTime? _,
                DateTime _,
                Guid? _,
                CancellationToken _) => observedLastProgressAt = lastProgressAt)
            .ReturnsAsync(true);
        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken token) => operation(token));
        var service = new DownloadService(
            [_integration.Object],
            Mock.Of<IDownloadClientRepository>(),
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            transactionManager.Object,
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);

        // Act
        await service.ApplyRefreshedClientStateAsync(download, info, null, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.That(
            observedLastProgressAt,
            Is.Not.EqualTo(new DateTime(2026, 8, 5, 11, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task DeleteAsync_ReturnsSuccess_WhenClientDeletesFilesAndRowWasAlreadyDeleted()
    {
        // Arrange
        var downloadId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var clientId = Guid.Parse("55555555-5555-5555-5555-555555555556");
        var settings = new Dictionary<string, string> { ["host"] = "127.0.0.1" };
        var download = new Download
        {
            Id = downloadId,
            ReleaseId = Guid.Parse("55555555-5555-5555-5555-555555555557"),
            DownloadClientId = clientId,
            ExternalId = "ext-deleted",
            Title = "Deleted download",
            Status = DownloadStatus.Imported,
            CreatedAt = DateTime.UtcNow
        };
        var clientRepository = new Mock<IDownloadClientRepository> { CallBase = true };
        clientRepository
            .Setup(r => r.GetByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadClient
            {
                Id = clientId,
                Name = "Mock client",
                ProviderType = "fake",
                Enabled = true,
                Status = IntegrationHealthStatus.Healthy,
                Settings = settings,
                CreatedAt = DateTime.UtcNow
            });
        _downloadRepository
            .Setup(r => r.GetByIdAsync(downloadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(download);
        _downloadRepository
            .Setup(r => r.DeleteAsync(downloadId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _integration
            .Setup(i => i.RemoveDownloadAsync("ext-deleted", true, settings, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = new DownloadService(
            [_integration.Object],
            clientRepository.Object,
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);

        // Act
        var result = await service.DeleteAsync(downloadId, deleteFiles: true).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task BulkRemoveAsync_RemovesFilteredTargetsAndDeletesRows_WhenFilterExcludesIds()
    {
        // Arrange
        var firstDownloadId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var secondDownloadId = Guid.NewGuid();
        var excludedDownloadId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var settings = new Dictionary<string, string> { ["host"] = "127.0.0.1" };
        var client = new DownloadClient
        {
            Id = clientId,
            Name = "Mock client",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = settings,
            CreatedAt = DateTime.UtcNow
        };
        var firstDownload = new Download
        {
            Id = firstDownloadId,
            ReleaseId = Guid.NewGuid(),
            DownloadClientId = clientId,
            ExternalId = "ext-first",
            Title = "First download",
            Status = DownloadStatus.Imported,
            CreatedAt = DateTime.UtcNow
        };
        var secondDownload = new Download
        {
            Id = secondDownloadId,
            ReleaseId = Guid.NewGuid(),
            DownloadClientId = clientId,
            ExternalId = "ext-second",
            Title = "Second download",
            Status = DownloadStatus.Downloaded,
            CreatedAt = DateTime.UtcNow
        };
        var filter = new DownloadQuery
        {
            Statuses = [DownloadStatus.Imported, DownloadStatus.Downloaded],
            ExcludeIds = [excludedDownloadId]
        };
        var clientRepository = new Mock<IDownloadClientRepository> { CallBase = true };
        clientRepository
            .Setup(r => r.GetByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _downloadRepository
            .Setup(r => r.QueryAsync(
                filter,
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 500),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items = [firstDownload, secondDownload],
                PageNumber = 1,
                PageSize = 500,
                TotalItems = 2
            });
        _downloadRepository
            .Setup(r => r.DeleteAsync(It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _integration
            .Setup(i => i.RemoveDownloadAsync(
                It.IsAny<string>(),
                true,
                settings,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = new DownloadService(
            [_integration.Object],
            clientRepository.Object,
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);

        // Act
        var result = await service
            .BulkRemoveAsync(new BulkAction<DownloadQuery> { Filter = filter }, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(2));
            _integration.Verify(
                i => i.RemoveDownloadAsync("ext-first", true, settings, It.IsAny<CancellationToken>()),
                Times.Once);
            _integration.Verify(
                i => i.RemoveDownloadAsync("ext-second", true, settings, It.IsAny<CancellationToken>()),
                Times.Once);
            _downloadRepository.Verify(
                r => r.DeleteAsync(firstDownloadId, null, It.IsAny<CancellationToken>()),
                Times.Once);
            _downloadRepository.Verify(
                r => r.DeleteAsync(secondDownloadId, null, It.IsAny<CancellationToken>()),
                Times.Once);
            _downloadRepository.Verify(
                r => r.DeleteAsync(excludedDownloadId, null, It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task BulkRemoveAsync_ReportsPartialFailureAndKeepsRow_WhenClientRemovalFails()
    {
        // Arrange
        var firstDownloadId = Guid.NewGuid();
        var secondDownloadId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var settings = new Dictionary<string, string> { ["host"] = "127.0.0.1" };
        var client = new DownloadClient
        {
            Id = clientId,
            Name = "Mock client",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = settings,
            CreatedAt = DateTime.UtcNow
        };
        var firstDownload = new Download
        {
            Id = firstDownloadId,
            ReleaseId = Guid.NewGuid(),
            DownloadClientId = clientId,
            ExternalId = "ext-failing",
            Title = "Failing client removal",
            Status = DownloadStatus.Imported,
            CreatedAt = DateTime.UtcNow
        };
        var secondDownload = new Download
        {
            Id = secondDownloadId,
            ReleaseId = Guid.NewGuid(),
            DownloadClientId = clientId,
            ExternalId = "ext-continuing",
            Title = "Continuing client removal",
            Status = DownloadStatus.Downloaded,
            CreatedAt = DateTime.UtcNow
        };
        var clientRepository = new Mock<IDownloadClientRepository> { CallBase = true };
        clientRepository
            .Setup(r => r.GetByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _downloadRepository
            .Setup(r => r.GetByIdAsync(firstDownloadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstDownload);
        _downloadRepository
            .Setup(r => r.GetByIdAsync(secondDownloadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondDownload);
        _downloadRepository
            .Setup(r => r.DeleteAsync(It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _integration
            .Setup(i => i.RemoveDownloadAsync(
                "ext-failing",
                true,
                settings,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("client removal failed"));
        _integration
            .Setup(i => i.RemoveDownloadAsync(
                "ext-continuing",
                true,
                settings,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = new DownloadService(
            [_integration.Object],
            clientRepository.Object,
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);

        // Act
        var result = await service
            .BulkRemoveAsync(
                new BulkAction<DownloadQuery> { Ids = [firstDownloadId, secondDownloadId] },
                CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result.Error, Is.EqualTo("Failed to remove 1 download."));
            _integration.Verify(
                i => i.RemoveDownloadAsync("ext-failing", true, settings, It.IsAny<CancellationToken>()),
                Times.Once);
            _integration.Verify(
                i => i.RemoveDownloadAsync("ext-continuing", true, settings, It.IsAny<CancellationToken>()),
                Times.Once);
            _downloadRepository.Verify(
                r => r.DeleteAsync(firstDownloadId, null, It.IsAny<CancellationToken>()),
                Times.Never);
            _downloadRepository.Verify(
                r => r.DeleteAsync(secondDownloadId, null, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task QueryAsync_StampsCapabilityFields_WhenIntegrationsSupportDifferentCapabilities()
    {
        // Arrange
        var pauseOnlyId = Guid.NewGuid();
        var resumeOnlyId = Guid.NewGuid();
        var forceOnlyId = Guid.NewGuid();
        var basicId = Guid.NewGuid();
        var pauseOnlyEntry = new DownloadQueueEntry
        {
            Id = pauseOnlyId,
            DownloadClientId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ProviderType = "pause-fake",
            ExternalId = "ext-pause",
            Title = "Pause-only download",
            Status = DownloadStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };
        var resumeOnlyEntry = new DownloadQueueEntry
        {
            Id = resumeOnlyId,
            DownloadClientId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ProviderType = "resume-fake",
            ExternalId = "ext-resume",
            Title = "Resume-only download",
            Status = DownloadStatus.Paused,
            CreatedAt = DateTime.UtcNow
        };
        var forceOnlyEntry = new DownloadQueueEntry
        {
            Id = forceOnlyId,
            DownloadClientId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ProviderType = "force-fake",
            ExternalId = "ext-force",
            Title = "Force-only download",
            Status = DownloadStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };
        var basicEntry = new DownloadQueueEntry
        {
            Id = basicId,
            DownloadClientId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            ProviderType = "basic-fake",
            ExternalId = "ext-basic",
            Title = "Basic download",
            Status = DownloadStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };
        var clientRepository = new Mock<IDownloadClientRepository> { CallBase = true };
        _downloadRepository
            .Setup(r => r.QueryQueueEntriesAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<DownloadQueueEntry>
            {
                Items = [pauseOnlyEntry, resumeOnlyEntry, forceOnlyEntry, basicEntry],
                PageNumber = 1,
                PageSize = 25,
                TotalItems = 4
            });
        var pauseOnly = new Mock<IDownloadClientIntegration>();
        pauseOnly.SetupGet(i => i.ProviderType).Returns("pause-fake");
        pauseOnly.As<IPausableDownloadClient>();
        var resumeOnly = new Mock<IDownloadClientIntegration>();
        resumeOnly.SetupGet(i => i.ProviderType).Returns("resume-fake");
        resumeOnly.As<IResumableDownloadClient>();
        var forceOnly = new Mock<IDownloadClientIntegration>();
        forceOnly.SetupGet(i => i.ProviderType).Returns("force-fake");
        forceOnly.As<IForcableDownloadClient>();
        var basic = new Mock<IDownloadClientIntegration>();
        basic.SetupGet(i => i.ProviderType).Returns("basic-fake");
        var service = new DownloadService(
            [pauseOnly.Object, resumeOnly.Object, forceOnly.Object, basic.Object],
            clientRepository.Object,
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);

        // Act
        var page = await service
            .QueryAsync(new DownloadQuery(), new PagingParameter { PageNumber = 1, PageSize = 25 }, null)
            .ConfigureAwait(false);

        // Assert
        var pauseResult = page.Items.Single(d => d.Id == pauseOnlyId);
        var resumeResult = page.Items.Single(d => d.Id == resumeOnlyId);
        var forceResult = page.Items.Single(d => d.Id == forceOnlyId);
        var basicResult = page.Items.Single(d => d.Id == basicId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pauseResult.CanPause, Is.True);
            Assert.That(pauseResult.CanResume, Is.False);
            Assert.That(pauseResult.CanForceStart, Is.False);
            Assert.That(resumeResult.CanPause, Is.False);
            Assert.That(resumeResult.CanResume, Is.True);
            Assert.That(resumeResult.CanForceStart, Is.False);
            Assert.That(forceResult.CanPause, Is.False);
            Assert.That(forceResult.CanResume, Is.False);
            Assert.That(forceResult.CanForceStart, Is.True);
            Assert.That(basicResult.CanPause, Is.False);
            Assert.That(basicResult.CanResume, Is.False);
            Assert.That(basicResult.CanForceStart, Is.False);
            clientRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(DownloadStatus.Grabbed, true, false, false)]
    [TestCase(DownloadStatus.Queued, true, false, true)]
    [TestCase(DownloadStatus.Downloading, true, false, false)]
    [TestCase(DownloadStatus.Paused, false, true, true)]
    [TestCase(DownloadStatus.Stalled, true, false, true)]
    [TestCase(DownloadStatus.Downloaded, false, false, false)]
    [TestCase(DownloadStatus.Imported, false, false, false)]
    [TestCase(DownloadStatus.Failed, false, false, false)]
    [TestCase(DownloadStatus.Cancelled, false, false, false)]
    [TestCase(DownloadStatus.Importing, false, false, false)]
    public async Task QueryAsync_StampsCapabilityFields_WhenStatusAndInterfacesAllow(
        DownloadStatus status,
        bool canPause,
        bool canResume,
        bool canForceStart)
    {
        // Arrange
        var downloadId = Guid.NewGuid();
        var entry = new DownloadQueueEntry
        {
            Id = downloadId,
            DownloadClientId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            ProviderType = "capable-fake",
            ExternalId = "ext-capable",
            Title = "Capable download",
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
        _downloadRepository
            .Setup(r => r.QueryQueueEntriesAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<DownloadQueueEntry>
            {
                Items = [entry],
                PageNumber = 1,
                PageSize = 25,
                TotalItems = 1
            });
        var capable = new Mock<IDownloadClientIntegration>();
        capable.SetupGet(i => i.ProviderType).Returns("capable-fake");
        capable.As<IPausableDownloadClient>();
        capable.As<IResumableDownloadClient>();
        capable.As<IForcableDownloadClient>();
        var service = new DownloadService(
            [capable.Object],
            Mock.Of<IDownloadClientRepository>(),
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);

        // Act
        var page = await service
            .QueryAsync(new DownloadQuery(), new PagingParameter { PageNumber = 1, PageSize = 25 }, null)
            .ConfigureAwait(false);

        // Assert
        var result = page.Items.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.CanPause, Is.EqualTo(canPause));
            Assert.That(result.CanResume, Is.EqualTo(canResume));
            Assert.That(result.CanForceStart, Is.EqualTo(canForceStart));
        }
    }

    [Test]
    public async Task GetAsync_StampsCapabilityFieldsFalse_WhenIntegrationMissing()
    {
        // Arrange
        var downloadId = Guid.NewGuid();
        var entry = new DownloadQueueEntry
        {
            Id = downloadId,
            DownloadClientId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ProviderType = "missing-fake",
            ExternalId = "ext-orphan",
            Title = "Orphan download",
            Status = DownloadStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };
        _downloadRepository
            .Setup(r => r.GetQueueEntryByIdAsync(downloadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        var service = new DownloadService(
            [_integration.Object],
            Mock.Of<IDownloadClientRepository>(),
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);

        // Act
        var result = await service.GetAsync(downloadId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.CanPause, Is.False);
            Assert.That(result.CanResume, Is.False);
            Assert.That(result.CanForceStart, Is.False);
        }
    }

    [Test]
    public async Task QueryAsync_StampsCapabilityFields_WhenMultipleDownloadsShareAClient()
    {
        // Arrange
        var downloadId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var entry = new DownloadQueueEntry
        {
            Id = downloadId,
            DownloadClientId = Guid.Parse("77777777-7777-7777-7777-777777777777"),
            ProviderType = "capable-fake",
            ExternalId = "ext-capable",
            Title = "Capable download",
            Status = DownloadStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };
        var clientRepository = new Mock<IDownloadClientRepository> { CallBase = true };
        clientRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Stored secret settings do not contain a protected envelope"));
        _downloadRepository
            .Setup(r => r.QueryQueueEntriesAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<DownloadQueueEntry>
            {
                Items = [entry],
                PageNumber = 1,
                PageSize = 25,
                TotalItems = 1
            });
        var capable = new Mock<IDownloadClientIntegration>();
        capable.SetupGet(i => i.ProviderType).Returns("capable-fake");
        capable.As<IPausableDownloadClient>();
        capable.As<IResumableDownloadClient>();
        capable.As<IForcableDownloadClient>();
        var service = new DownloadService(
            [capable.Object],
            clientRepository.Object,
            _downloadRepository.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            TimeProvider.System,
            NullLogger<DownloadService>.Instance);

        // Act
        var page = await service
            .QueryAsync(new DownloadQuery(), new PagingParameter { PageNumber = 1, PageSize = 25 }, null)
            .ConfigureAwait(false);

        // Assert
        var result = page.Items.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.CanPause, Is.True);
            Assert.That(result.CanResume, Is.False);
            Assert.That(result.CanForceStart, Is.True);
            clientRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task TryApplyVerifiedClientStateAsync_AppliesTheCompletedStatus_WhenTheClientReportsDownloaded()
    {
        // Act
        await _subject
            .TryApplyVerifiedClientStateAsync(_candidate, _downloadedInfo, _now, _now, null, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_refreshedDownloadId, Is.EqualTo(new Guid("65bf4f33-77dc-4ae7-a4b9-9d89012df7b6")));
            Assert.That(_refreshedStatus, Is.EqualTo(DownloadStatus.Downloaded));
        }
    }

    [Test]
    public async Task TryApplyVerifiedClientStateAsync_DoesNotEnqueueImport_WhenTheDownloadCompletes()
    {
        // Act
        await _subject
            .TryApplyVerifiedClientStateAsync(_candidate, _downloadedInfo, _now, _now, null, default)
            .ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            manager => manager.ExecuteAsync(
                It.IsAny<JobEnqueueRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
