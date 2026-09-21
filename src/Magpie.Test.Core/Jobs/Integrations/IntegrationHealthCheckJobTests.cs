using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Integrations;

[TestFixture]
public sealed class IntegrationHealthCheckJobTests
{
    private readonly IProgressReporter _progressReporter = Mock.Of<IProgressReporter>();

    private Mock<IIndexerConnectionRepository> _indexerConnectionRepository = null!;
    private Mock<IDownloadClientRepository> _downloadClientRepository = null!;
    private Mock<IIndexerService> _indexerService = null!;
    private Mock<IMediaServerService> _mediaServerService = null!;
    private Mock<IEventBus> _eventBus = null!;

    [SetUp]
    public void SetUp()
    {
        _indexerConnectionRepository = new Mock<IIndexerConnectionRepository>();
        _indexerConnectionRepository.Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _downloadClientRepository = new Mock<IDownloadClientRepository>();
        _downloadClientRepository.Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _downloadClientRepository
            .Setup(r => r.PatchAsync(It.IsAny<DownloadClientPatch>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indexerService = new Mock<IIndexerService>();
        _mediaServerService = new Mock<IMediaServerService>();
        _mediaServerService.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _eventBus = new Mock<IEventBus>();
    }

    [Test]
    public void Describe_ReturnsAdministrativeHourlySpec_WhenRead()
    {
        // Arrange
        var spec = IntegrationHealthCheckJob.Spec;

        // Act
        var staleAfter = spec.Parameters.FirstOrDefault(p =>
            p.Name == "staleAfterMinutes");
        var timeout = spec.Parameters.FirstOrDefault(
            p => p.Name == "timeoutSeconds");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spec.JobType, Is.EqualTo("IntegrationHealthCheck"));
            Assert.That(spec.Scope, Is.EqualTo(JobScope.Administrative));
            Assert.That(spec.SupportedTriggerModes, Does.Contain(JobTriggerMode.Schedule));
            Assert.That(staleAfter, Is.Not.Null);
            Assert.That(staleAfter!.DefaultValue, Is.EqualTo("60"));
            Assert.That(staleAfter.Type, Is.EqualTo(DataType.Number));
            Assert.That(timeout, Is.Not.Null);
            Assert.That(timeout!.DefaultValue, Is.EqualTo("10"));
            Assert.That(timeout.Type, Is.EqualTo(DataType.Number));
        }
    }

    [Test]
    public async Task ExecuteAsync_ProbesClientAndPublishesHealthChange_WhenStaleEnabledStatusChanges()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            ProviderType = "qbit",
            Name = "qb",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            LastChecked = null
        };
        _downloadClientRepository
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([client]);
        _downloadClientRepository
            .SetupSequence(r => r.GetByIdAsync(client.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client)
            .ReturnsAsync(client with { Status = IntegrationHealthStatus.Healthy });
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("qbit");
        integration
            .Setup(i => i.TestConnectionAsync(
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigurationTestResult { Success = true, Message = "ok" });
        var downloadClientService = new DownloadClientService(
            _downloadClientRepository.Object,
            [integration.Object],
            new Mock<ISettingsService>().Object,
            _eventBus.Object,
            TimeProvider.System);
        var subject = new IntegrationHealthCheckJob(
            _indexerConnectionRepository.Object,
            _indexerService.Object,
            downloadClientService,
            _mediaServerService.Object,
            TimeProvider.System,
            NullLogger<IntegrationHealthCheckJob>.Instance,
            _progressReporter);
        var jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        // Act
        await subject.ExecuteAsync(jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadClientRepository.Verify(
                r => r.PatchAsync(
                    It.Is<DownloadClientPatch>(p =>
                        p.Status != null && p.Status.Value == IntegrationHealthStatus.Healthy),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _eventBus.Verify(
                b => b.PublishAsync(
                    It.Is<MagpieEvent>(e => e.Name == "integration.health.changed"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsFreshEnabledDownloadClient_WhenClientHealthIsFresh()
    {
        // Arrange
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            ProviderType = "qbit",
            Name = "qb",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            LastChecked = DateTime.UtcNow
        };
        _downloadClientRepository
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([client]);
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("qbit");
        var downloadClientService = new DownloadClientService(
            _downloadClientRepository.Object,
            [integration.Object],
            new Mock<ISettingsService>().Object,
            _eventBus.Object,
            TimeProvider.System);
        var subject = new IntegrationHealthCheckJob(
            _indexerConnectionRepository.Object,
            _indexerService.Object,
            downloadClientService,
            _mediaServerService.Object,
            TimeProvider.System,
            NullLogger<IntegrationHealthCheckJob>.Instance,
            _progressReporter);
        var jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        // Act
        await subject.ExecuteAsync(jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            integration.Verify(
                i => i.TestConnectionAsync(
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _downloadClientRepository.Verify(
                r => r.PatchAsync(
                    It.IsAny<DownloadClientPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _eventBus.Verify(b => b.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
