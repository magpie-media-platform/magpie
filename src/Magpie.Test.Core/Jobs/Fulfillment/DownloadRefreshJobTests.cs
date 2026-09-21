using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs.Downloads;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class DownloadRefreshJobTests
{
    private readonly IProgressReporter _progressReporter = Mock.Of<IProgressReporter>();

    private List<IDownloadClientIntegration> _integrations = null!;
    private List<DownloadClient> _clients = null!;
    private Dictionary<Guid, List<Download>> _downloadsByClient = null!;
    private Mock<IDownloadClientRepository> _downloadClientRepository = null!;
    private Mock<IDownloadRepository> _downloadRepository = null!;
    private Mock<IJobManager> _jobManager = null!;
    private Mock<IDownloadService> _downloadService = null!;
    private FakeTimeProvider _timeProvider = null!;
    private IJob _subject = null!;
    private JobContext _jobContext = null!;
    private Guid _userId;
    private DateTime _now;

    [SetUp]
    public void SetUp()
    {
        _now = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(_now, TimeSpan.Zero));

        _integrations = [];
        _clients = [];
        _downloadsByClient = [];

        _downloadClientRepository = new Mock<IDownloadClientRepository> { CallBase = true };
        _downloadClientRepository
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [.. _clients]);

        _downloadRepository = new Mock<IDownloadRepository> { CallBase = true };
        _downloadRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DownloadQuery query, PagingParameter _, SortParameter? _, CancellationToken _) =>
            {
                var items = query.DownloadClientId is { } clientId &&
                    _downloadsByClient.TryGetValue(clientId, out var downloads)
                        ? downloads.ToArray()
                        : [];
                return new PagedList<Download> { Items = items };
            });

        _jobManager = new Mock<IJobManager>();
        _jobManager
            .Setup(r => r.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _downloadService = new Mock<IDownloadService>();

        _subject = new DownloadRefreshJob(
            _integrations,
            _downloadClientRepository.Object,
            _downloadRepository.Object,
            _jobManager.Object,
            NullLogger<DownloadRefreshJob>.Instance,
            _downloadService.Object,
            _timeProvider,
            _progressReporter);

        _userId = Guid.NewGuid();
        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);
    }

    [Test]
    public async Task ExecuteAsync_DoesNothing_WhenNoEnabledDownloadClientsExist()
    {
        // Arrange

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _downloadRepository.Verify(
            r => r.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_SkipsClient_WhenClientIsDisabled()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Disabled",
            ProviderType = "fake",
            Enabled = false,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-disabled",
                Title = "Download",
                Status = DownloadStatus.Downloading,
                CreatedAt = DateTime.UtcNow
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        integration.Verify(
            i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_SkipsClient_WhenNoIntegrationIsRegisteredForProviderType()
    {
        // Arrange
        var unknown = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Unknown",
            ProviderType = "missing",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        var known = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Known",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _clients.Add(unknown);
        _clients.Add(known);
        var knownDownload = new Download
        {
            Id = Guid.NewGuid(),
            DownloadClientId = known.Id,
            ExternalId = "ext-known",
            Title = "Download",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        _downloadsByClient[known.Id] = [knownDownload];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-known"] = new DownloadInfo { ExternalId = "ext-known", Status = DownloadStatus.Downloaded }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadRepository.Verify(
                r => r.QueryAsync(
                    It.Is<DownloadQuery>(q => q.DownloadClientId == unknown.Id),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _downloadService.Verify(
                s => s.ApplyRefreshedClientStateAsync(
                    It.Is<Download>(d => d.Id == knownDownload.Id),
                    It.IsAny<DownloadInfo>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_BatchesOnlyNonTerminalDownloads_WhenTheClientIsEnabled()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-grabbed",
                Title = "Download ext-grabbed",
                Status = DownloadStatus.Grabbed,
                CreatedAt = DateTime.UtcNow
            },
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-downloading",
                Title = "Download ext-downloading",
                Status = DownloadStatus.Downloading,
                CreatedAt = DateTime.UtcNow
            },
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-imported",
                Title = "Download",
                Status = DownloadStatus.Imported,
                ClientRemovedAt = DateTime.UtcNow.AddMinutes(-1),
                CreatedAt = DateTime.UtcNow
            },
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-failed",
                Title = "Download ext-failed",
                Status = DownloadStatus.Failed,
                CreatedAt = DateTime.UtcNow
            },
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-cancelled",
                Title = "Download ext-cancelled",
                Status = DownloadStatus.Cancelled,
                CreatedAt = DateTime.UtcNow
            }
        ];
        IEnumerable<string>? capturedIds = null;
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, IReadOnlyDictionary<string, string>, CancellationToken>(
                (ids, _, _) => capturedIds = [.. ids])
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal),
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedIds, Is.EquivalentTo(["ext-grabbed", "ext-downloading"]));
            integration.Verify(
                i => i.GetDownloadsAsync(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_AppliesRefreshedClientState_WhenIntegrationResultIsProvided()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _clients.Add(client);
        var download = new Download
        {
            Id = Guid.NewGuid(),
            DownloadClientId = client.Id,
            ExternalId = "ext-1",
            Title = "Download ext-1",
            Status = DownloadStatus.Grabbed,
            CreatedAt = DateTime.UtcNow
        };
        _downloadsByClient[client.Id] = [download];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-1"] = new DownloadInfo
                    {
                        ExternalId = "ext-1",
                        Status = DownloadStatus.Downloading,
                        StatusReason = "in progress",
                        OutputPath = "/dl/ext-1"
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _downloadService.Verify(
            s => s.ApplyRefreshedClientStateAsync(
                It.Is<Download>(d => d.Id == download.Id),
                It.Is<DownloadInfo>(info => info.ExternalId == "ext-1" && info.Status == DownloadStatus.Downloading),
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SchedulesFiveSecondSuccessor_WhenEtaIsExactlyOneMinute()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = _now
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-1",
                Title = "Download ext-1",
                Status = DownloadStatus.Downloading,
                Progress = 0m,
                LastProgressAt = _now.AddSeconds(-60),
                CreatedAt = _now.AddSeconds(-60)
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-1"] = new DownloadInfo
                    {
                        ExternalId = "ext-1",
                        Status = DownloadStatus.Downloading,
                        ProgressPercent = 50m
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            r => r.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "DownloadRefresh" &&
                    request.Mode == JobExecutionMode.Queue &&
                    request.NotBeforeAt == _now.AddSeconds(5) &&
                    request.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_MarksTheRequestAsSuccessor_WhenItSchedulesTheNextPoll()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = _now
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-1",
                Title = "Download ext-1",
                Status = DownloadStatus.Downloading,
                Progress = 0m,
                LastProgressAt = _now.AddSeconds(-60),
                CreatedAt = _now.AddSeconds(-60)
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-1"] = new DownloadInfo
                    {
                        ExternalId = "ext-1",
                        Status = DownloadStatus.Downloading,
                        ProgressPercent = 50m
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            r => r.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "DownloadRefresh" &&
                    request.IsSuccessor &&
                    request.CorrelationId == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SchedulesFifteenSecondSuccessor_WhenEtaIsExactlyFiveMinutes()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = _now
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-1",
                Title = "Download ext-1",
                Status = DownloadStatus.Downloading,
                Progress = 0m,
                LastProgressAt = _now.AddSeconds(-300),
                CreatedAt = _now.AddSeconds(-300)
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-1"] = new DownloadInfo
                    {
                        ExternalId = "ext-1",
                        Status = DownloadStatus.Downloading,
                        ProgressPercent = 50m
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            r => r.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "DownloadRefresh" &&
                    request.Mode == JobExecutionMode.Queue &&
                    request.NotBeforeAt == _now.AddSeconds(15) &&
                    request.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SchedulesThirtySecondSuccessor_WhenEtaIsExactlyFifteenMinutes()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = _now
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-1",
                Title = "Download ext-1",
                Status = DownloadStatus.Downloading,
                Progress = 0m,
                LastProgressAt = _now.AddSeconds(-900),
                CreatedAt = _now.AddSeconds(-900)
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-1"] = new DownloadInfo
                    {
                        ExternalId = "ext-1",
                        Status = DownloadStatus.Downloading,
                        ProgressPercent = 50m
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            r => r.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "DownloadRefresh" &&
                    request.Mode == JobExecutionMode.Queue &&
                    request.NotBeforeAt == _now.AddSeconds(30) &&
                    request.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SchedulesSixtySecondSuccessor_WhenEtaExceedsFifteenMinutes()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = _now
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-1",
                Title = "Download ext-1",
                Status = DownloadStatus.Downloading,
                Progress = 0m,
                LastProgressAt = _now.AddSeconds(-1200),
                CreatedAt = _now.AddSeconds(-1200)
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-1"] = new DownloadInfo
                    {
                        ExternalId = "ext-1",
                        Status = DownloadStatus.Downloading,
                        ProgressPercent = 50m
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            r => r.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "DownloadRefresh" &&
                    request.Mode == JobExecutionMode.Queue &&
                    request.NotBeforeAt == _now.AddSeconds(60) &&
                    request.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SchedulesFifteenSecondSuccessor_WhenFormingTheFirstUsableBaseline()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = _now
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-1",
                Title = "Download ext-1",
                Status = DownloadStatus.Downloading,
                Progress = null,
                LastProgressAt = null,
                CreatedAt = _now.AddSeconds(-60)
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-1"] = new DownloadInfo
                    {
                        ExternalId = "ext-1",
                        Status = DownloadStatus.Downloading,
                        ProgressPercent = 25m
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            r => r.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "DownloadRefresh" &&
                    request.Mode == JobExecutionMode.Queue &&
                    request.NotBeforeAt == _now.AddSeconds(15) &&
                    request.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SchedulesFifteenSecondSuccessor_WhenDownloadTransitionsIntoActive()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = _now
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-1",
                Title = "Download ext-1",
                Status = DownloadStatus.Grabbed,
                Progress = 0m,
                LastProgressAt = _now.AddSeconds(-60),
                CreatedAt = _now.AddSeconds(-60)
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-1"] = new DownloadInfo
                    {
                        ExternalId = "ext-1",
                        Status = DownloadStatus.Downloading,
                        ProgressPercent = 50m
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            r => r.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "DownloadRefresh" &&
                    request.Mode == JobExecutionMode.Queue &&
                    request.NotBeforeAt == _now.AddSeconds(15) &&
                    request.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SchedulesFiveSecondSuccessor_WhenActiveDownloadReportsComplete()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = _now
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-1",
                Title = "Download ext-1",
                Status = DownloadStatus.Downloading,
                Progress = 90m,
                LastProgressAt = _now.AddSeconds(-60),
                CreatedAt = _now.AddSeconds(-60)
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-1"] = new DownloadInfo
                    {
                        ExternalId = "ext-1",
                        Status = DownloadStatus.Downloading,
                        ProgressPercent = 100m
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            r => r.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "DownloadRefresh" &&
                    request.Mode == JobExecutionMode.Queue &&
                    request.NotBeforeAt == _now.AddSeconds(5) &&
                    request.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SchedulesSixtySecondSuccessor_WhenActiveButQueued()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = _now
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-1",
                Title = "Download ext-1",
                Status = DownloadStatus.Downloading,
                Progress = 40m,
                LastProgressAt = _now.AddSeconds(-60),
                CreatedAt = _now.AddSeconds(-60)
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-1"] = new DownloadInfo
                    {
                        ExternalId = "ext-1",
                        Status = DownloadStatus.Queued,
                        ProgressPercent = 40m
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            r => r.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "DownloadRefresh" &&
                    request.Mode == JobExecutionMode.Queue &&
                    request.NotBeforeAt == _now.AddSeconds(60) &&
                    request.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SchedulesNoSuccessor_WhenNoDownloadIsActive()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-seed",
                Title = "Download ext-seed",
                Status = DownloadStatus.Downloading,
                CreatedAt = DateTime.UtcNow
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-seed"] = new DownloadInfo { ExternalId = "ext-seed", Status = DownloadStatus.Downloaded }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            r => r.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_SchedulesSixtySecondSuccessor_WhenProgressDoesNotMove()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = _now
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-1",
                Title = "Download ext-1",
                Status = DownloadStatus.Downloading,
                Progress = 40m,
                LastProgressAt = _now.AddSeconds(-60),
                CreatedAt = _now.AddSeconds(-60)
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-1"] = new DownloadInfo
                    {
                        ExternalId = "ext-1",
                        Status = DownloadStatus.Downloading,
                        ProgressPercent = 40m
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            r => r.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "DownloadRefresh" &&
                    request.Mode == JobExecutionMode.Queue &&
                    request.NotBeforeAt == _now.AddSeconds(60) &&
                    request.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SchedulesSixtySecondSuccessor_WhenProgressRegresses()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = _now
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-1",
                Title = "Download ext-1",
                Status = DownloadStatus.Downloading,
                Progress = 60m,
                LastProgressAt = _now.AddSeconds(-60),
                CreatedAt = _now.AddSeconds(-60)
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-1"] = new DownloadInfo
                    {
                        ExternalId = "ext-1",
                        Status = DownloadStatus.Downloading,
                        ProgressPercent = 50m
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            r => r.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "DownloadRefresh" &&
                    request.Mode == JobExecutionMode.Queue &&
                    request.NotBeforeAt == _now.AddSeconds(60) &&
                    request.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SchedulesSixtySecondSuccessor_WhenProgressIsMissing()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = _now
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-1",
                Title = "Download ext-1",
                Status = DownloadStatus.Downloading,
                Progress = 30m,
                LastProgressAt = _now.AddSeconds(-60),
                CreatedAt = _now.AddSeconds(-60)
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-1"] = new DownloadInfo
                    {
                        ExternalId = "ext-1",
                        Status = DownloadStatus.Downloading,
                        ProgressPercent = null
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            r => r.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "DownloadRefresh" &&
                    request.Mode == JobExecutionMode.Queue &&
                    request.NotBeforeAt == _now.AddSeconds(60) &&
                    request.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SchedulesShortestSuccessorAndEnqueuesOnce_WhenSeveralDownloadsAreActive()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = _now
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-slow",
                Title = "Download ext-slow",
                Status = DownloadStatus.Downloading,
                Progress = 40m,
                LastProgressAt = _now.AddSeconds(-60),
                CreatedAt = _now.AddSeconds(-60)
            },
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-fast",
                Title = "Download ext-fast",
                Status = DownloadStatus.Downloading,
                Progress = 0m,
                LastProgressAt = _now.AddSeconds(-60),
                CreatedAt = _now.AddSeconds(-60)
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-slow"] = new DownloadInfo
                    {
                        ExternalId = "ext-slow",
                        Status = DownloadStatus.Downloading,
                        ProgressPercent = 41m
                    },
                    ["ext-fast"] = new DownloadInfo
                    {
                        ExternalId = "ext-fast",
                        Status = DownloadStatus.Downloading,
                        ProgressPercent = 50m
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(
                r => r.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(request =>
                        request.JobType == "DownloadRefresh" &&
                        request.Mode == JobExecutionMode.Queue &&
                        request.NotBeforeAt == _now.AddSeconds(5) &&
                        request.UserId == _userId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _jobManager.Verify(
                r => r.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesSuccessor_WhenTheRefreshFinishes()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = _now
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-1",
                Title = "Download ext-1",
                Status = DownloadStatus.Downloading,
                Progress = 0m,
                LastProgressAt = _now.AddSeconds(-60),
                CreatedAt = _now.AddSeconds(-60)
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-1"] = new DownloadInfo
                    {
                        ExternalId = "ext-1",
                        Status = DownloadStatus.Downloading,
                        ProgressPercent = 50m
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            r => r.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "DownloadRefresh" &&
                    request.Mode == JobExecutionMode.Queue &&
                    request.NotBeforeAt == _now.AddSeconds(5) &&
                    request.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_AppliesImportedHoldTracking_WhenTheClientHoldsAnImportedDownload()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _clients.Add(client);
        var download = new Download
        {
            Id = Guid.NewGuid(),
            DownloadClientId = client.Id,
            ExternalId = "ext-held",
            Title = "Download",
            Status = DownloadStatus.Imported,
            HeldByPolicy = true,
            ClientRemovedAt = null,
            CreatedAt = DateTime.UtcNow
        };
        _downloadsByClient[client.Id] = [download];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-held"] = new DownloadInfo
                    {
                        ExternalId = "ext-held",
                        Status = DownloadStatus.Downloaded,
                        HeldByPolicy = false
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadService.Verify(
                s => s.ApplyImportedHoldTrackingAsync(
                    It.Is<Download>(d => d.Id == download.Id),
                    It.Is<DownloadInfo>(info => info.ExternalId == "ext-held"),
                    _userId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _downloadService.Verify(
                s => s.ApplyRefreshedClientStateAsync(
                    It.IsAny<Download>(),
                    It.IsAny<DownloadInfo>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsImportedDownload_WhenClientRemovedAtIsSet()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-removed",
                Title = "Download",
                Status = DownloadStatus.Imported,
                HeldByPolicy = true,
                ClientRemovedAt = DateTime.UtcNow.AddMinutes(-5),
                CreatedAt = DateTime.UtcNow
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
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
                s => s.ApplyImportedHoldTrackingAsync(
                    It.IsAny<Download>(),
                    It.IsAny<DownloadInfo>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsImportedDownload_WhenNotHeldByPolicy()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-not-held",
                Title = "Download",
                Status = DownloadStatus.Imported,
                HeldByPolicy = false,
                ClientRemovedAt = null,
                CreatedAt = DateTime.UtcNow
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
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
                s => s.ApplyImportedHoldTrackingAsync(
                    It.IsAny<Download>(),
                    It.IsAny<DownloadInfo>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_ContinuesProcessingSiblings_WhenApplyRefreshedStateThrowsForOne()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _clients.Add(client);
        var failing = new Download
        {
            Id = Guid.NewGuid(),
            DownloadClientId = client.Id,
            ExternalId = "ext-fail",
            Title = "Download ext-fail",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        var succeeding = new Download
        {
            Id = Guid.NewGuid(),
            DownloadClientId = client.Id,
            ExternalId = "ext-ok",
            Title = "Download ext-ok",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        _downloadsByClient[client.Id] = [failing, succeeding];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-fail"] = new DownloadInfo { ExternalId = "ext-fail", Status = DownloadStatus.Downloading },
                    ["ext-ok"] = new DownloadInfo { ExternalId = "ext-ok", Status = DownloadStatus.Downloading }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);
        _downloadService
            .Setup(s => s.ApplyRefreshedClientStateAsync(
                It.Is<Download>(d => d.Id == failing.Id),
                It.IsAny<DownloadInfo>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _downloadService.Verify(
            s => s.ApplyRefreshedClientStateAsync(
                It.Is<Download>(d => d.Id == succeeding.Id),
                It.IsAny<DownloadInfo>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ContinuesProcessingSiblingClients_WhenOneClientBatchThrows()
    {
        // Arrange
        var failingClient = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "A-Failing",
            ProviderType = "fake-fail",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        var workingClient = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "B-Working",
            ProviderType = "fake-ok",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _clients.Add(failingClient);
        _clients.Add(workingClient);
        _downloadsByClient[failingClient.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = failingClient.Id,
                ExternalId = "ext-fc",
                Title = "Download ext-fc",
                Status = DownloadStatus.Downloading,
                CreatedAt = DateTime.UtcNow
            }
        ];
        var workingDownload = new Download
        {
            Id = Guid.NewGuid(),
            DownloadClientId = workingClient.Id,
            ExternalId = "ext-wc",
            Title = "Download ext-wc",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        _downloadsByClient[workingClient.Id] = [workingDownload];
        var failing = new Mock<IDownloadClientIntegration>();
        failing.SetupGet(i => i.ProviderType).Returns("fake-fail");
        failing
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("client down"));
        var working = new Mock<IDownloadClientIntegration>();
        working.SetupGet(i => i.ProviderType).Returns("fake-ok");
        working
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-wc"] = new DownloadInfo { ExternalId = "ext-wc", Status = DownloadStatus.Downloaded }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(failing.Object);
        _integrations.Add(working.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _downloadService.Verify(
            s => s.ApplyRefreshedClientStateAsync(
                It.Is<Download>(d => d.Id == workingDownload.Id),
                It.IsAny<DownloadInfo>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_PassesClientSettings_WhenQueryingTheClient()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "localhost",
                ["apiKey"] = "secret"
            },
            CreatedAt = DateTime.UtcNow
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-1",
                Title = "Download ext-1",
                Status = DownloadStatus.Downloading,
                CreatedAt = DateTime.UtcNow
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal),
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        integration.Verify(
            i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.Is<IReadOnlyDictionary<string, string>>(s =>
                    s.ContainsKey("host") &&
                    s["host"] == "localhost" &&
                    s.ContainsKey("apiKey") &&
                    s["apiKey"] == "secret"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_AppliesResolvedStatusesAndSkipsFailures_WhenBatchContainsPerIdFailures()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "BatchClient",
            ProviderType = "batch",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _clients.Add(client);
        var failingDownload = new Download
        {
            Id = Guid.NewGuid(),
            DownloadClientId = client.Id,
            ExternalId = "ext-fail",
            Title = "Download ext-fail",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        var goodDownload = new Download
        {
            Id = Guid.NewGuid(),
            DownloadClientId = client.Id,
            ExternalId = "ext-ok",
            Title = "Download ext-ok",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        _downloadsByClient[client.Id] = [failingDownload, goodDownload];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("batch");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-ok"] = new DownloadInfo { ExternalId = "ext-ok", Status = DownloadStatus.Downloaded }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)
                {
                    ["ext-fail"] = new InvalidOperationException("transient")
                }));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadService.Verify(
                s => s.ApplyRefreshedClientStateAsync(
                    It.Is<Download>(d => d.Id == goodDownload.Id),
                    It.IsAny<DownloadInfo>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _downloadService.Verify(
                s => s.ApplyRefreshedClientStateAsync(
                    It.Is<Download>(d => d.Id == failingDownload.Id),
                    It.IsAny<DownloadInfo>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_PropagatesCancellation_WhenTheTokenIsAlreadyCancelled()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "ClientA",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-a",
                Title = "Download ext-a",
                Status = DownloadStatus.Downloading,
                CreatedAt = DateTime.UtcNow
            }
        ];
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (IEnumerable<string> _, IReadOnlyDictionary<string, string> _, CancellationToken ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                return new BatchDownloadStatusResult(
                    new Dictionary<string, DownloadInfo>(StringComparer.Ordinal),
                    new Dictionary<string, Exception>(StringComparer.Ordinal));
            });
        _integrations.Add(integration.Object);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        // Act
        var caught = Assert.CatchAsync<OperationCanceledException>(
            () => _subject.ExecuteAsync(_jobContext, cts.Token));

        // Assert
        Assert.That(caught, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteAsync_AnchorsSuccessorOnSchedulingTime_WhenPassOutlastsChosenDelay()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = [],
            CreatedAt = _now
        };
        _clients.Add(client);
        _downloadsByClient[client.Id] =
        [
            new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = client.Id,
                ExternalId = "ext-1",
                Title = "Download ext-1",
                Status = DownloadStatus.Downloading,
                Progress = 0m,
                LastProgressAt = _now,
                CreatedAt = _now
            }
        ];
        var passDuration = TimeSpan.FromSeconds(10);
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => _timeProvider.Advance(passDuration))
            .ReturnsAsync(new BatchDownloadStatusResult(
                new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
                {
                    ["ext-1"] = new DownloadInfo
                    {
                        ExternalId = "ext-1",
                        Status = DownloadStatus.Downloading,
                        ProgressPercent = 50m
                    }
                },
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var completedAt = _now.Add(passDuration);
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(
                r => r.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(request =>
                        request.JobType == "DownloadRefresh" &&
                        request.Mode == JobExecutionMode.Queue &&
                        request.NotBeforeAt == completedAt.AddSeconds(5) &&
                        request.NotBeforeAt > completedAt &&
                        request.UserId == _userId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
