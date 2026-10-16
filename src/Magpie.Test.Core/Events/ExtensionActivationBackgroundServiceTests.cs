using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Database;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Events;

[TestFixture]
public sealed class ExtensionActivationBackgroundServiceTests
{
    private const string LocalNodeId = "node-1";

    private static readonly ExtensionNodeStatus[] NodesThatDoNotHoldBackTheRestart =
    [
        new()
        {
            NodeId = "node-0",
            AppliedGeneration = 2,
            ExtensionHealth = ExtensionNodeHealth.CurrentHealthy,
            SchedulerEligible = true,
            LastSeenAt = new DateTime(2026, 10, 12, 11, 59, 30, DateTimeKind.Utc),
            Message = "Extension activation succeeded."
        },
        new()
        {
            NodeId = "node-0",
            AppliedGeneration = 1,
            ExtensionHealth = ExtensionNodeHealth.StaleRequiresRestart,
            LastSeenAt = new DateTime(2026, 10, 12, 11, 57, 59, DateTimeKind.Utc),
            Message = "Extension changes require a restart."
        },
        new()
        {
            NodeId = "node-2",
            AppliedGeneration = 1,
            ExtensionHealth = ExtensionNodeHealth.StaleRequiresRestart,
            LastSeenAt = new DateTime(2026, 10, 12, 11, 59, 30, DateTimeKind.Utc),
            Message = "Extension changes require a restart."
        },
    ];

    private Mock<IDatabaseManagementProvider> _databaseProvider = null!;
    private Mock<IExtensionNodeStatusRepository> _nodeStatuses = null!;
    private Mock<IExtensionActivationGenerationRepository> _generations = null!;
    private Mock<IMagpieSystem> _system = null!;
    private Mock<IServiceScopeFactory> _scopeFactory = null!;
    private HostStatusInfo _hostStatus = null!;
    private DatabaseMigrationState _migrationState = null!;
    private ExtensionActivationBackgroundService.DatabaseFailureInterceptor _databaseFailures = null!;
    private FakeTimeProvider _timeProvider = null!;
    private MagpieHostEnvironment _hostEnvironment = null!;
    private EventBus _eventBus = null!;
    private ExtensionActivationBackgroundService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _databaseProvider = new Mock<IDatabaseManagementProvider>();
        _databaseProvider
            .Setup(provider => provider.GetUnknownMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _nodeStatuses = new Mock<IExtensionNodeStatusRepository>();
        _nodeStatuses
            .Setup(repository => repository.GetByNodeIdAsync(LocalNodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExtensionNodeStatus
            {
                NodeId = LocalNodeId,
                AppliedGeneration = 1,
                ExtensionHealth = ExtensionNodeHealth.CurrentHealthy,
                SchedulerEligible = true,
                LastSeenAt = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc)
            });
        _generations = new Mock<IExtensionActivationGenerationRepository>();
        _generations.Setup(repository => repository.GetCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _system = new Mock<IMagpieSystem>();

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IDatabaseManagementProvider)))
            .Returns(_databaseProvider.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IExtensionNodeStatusRepository)))
            .Returns(_nodeStatuses.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IExtensionActivationGenerationRepository)))
            .Returns(_generations.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IMagpieSystem)))
            .Returns(_system.Object);
        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(scope => scope.ServiceProvider).Returns(serviceProvider.Object);
        _scopeFactory = new Mock<IServiceScopeFactory>();
        _scopeFactory.Setup(factory => factory.CreateScope()).Returns(serviceScope.Object);

        _hostStatus = new HostStatusInfo { Mode = HostMode.Sentinel };
        _migrationState = new DatabaseMigrationState();
        _databaseFailures = new ExtensionActivationBackgroundService.DatabaseFailureInterceptor();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
        _hostEnvironment = new MagpieHostEnvironment(
            LocalNodeId,
            IsClustered: true,
            IsSupervised: true,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc));
        _eventBus = new EventBus(_hostEnvironment, Mock.Of<IJobExecutionScopeFactory>(), _timeProvider);
        _subject = new ExtensionActivationBackgroundService(
            _eventBus,
            _scopeFactory.Object,
            _hostEnvironment,
            _hostStatus,
            _timeProvider,
            NullLogger<ExtensionActivationBackgroundService>.Instance,
            _migrationState,
            _databaseFailures);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _subject.StopAsync(CancellationToken.None).ConfigureAwait(false);
        _subject.Dispose();
        _eventBus.Dispose();
    }

    [TestCase(HostMode.Sentinel)]
    [TestCase(HostMode.WindowsService)]
    public async Task RefreshNodeStatusAsync_RequestsRestartAtOnce_WhenAStaleNodeThatCanRestartItselfIsAlone(
        HostMode mode)
    {
        // Arrange
        _hostStatus.Mode = mode;

        // Act
        await _subject.RefreshNodeStatusAsync(2, CancellationToken.None).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _system.Verify(system => system.RequestRestartAsync(null, It.IsAny<CancellationToken>()), Times.Once);
            _nodeStatuses.Verify(
                repository => repository.UpsertAsync(
                    It.Is<ExtensionNodeStatus>(status =>
                        status.ExtensionHealth == ExtensionNodeHealth.StaleRequiresRestart &&
                        !status.SchedulerEligible),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task RefreshNodeStatusAsync_RequestsRestartOnce_WhenEventAndHeartbeatBothFindTheNodeStale()
    {
        // Arrange
        _system
            .Setup(system => system.RequestRestartAsync(null, It.IsAny<CancellationToken>()))
            .Callback(() => _hostStatus.IsShutdownRequested = true)
            .Returns(Task.CompletedTask);

        // Act
        await _subject.RefreshNodeStatusAsync(2, CancellationToken.None).ConfigureAwait(false);
        await _subject.RefreshNodeStatusAsync(null, CancellationToken.None).ConfigureAwait(false);

        // Assert
        _system.Verify(system => system.RequestRestartAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCaseSource(nameof(NodesThatDoNotHoldBackTheRestart))]
    public async Task RefreshNodeStatusAsync_RequestsRestartAtOnce_WhenNoNodeRankedBeforeItHoldsItBack(
        ExtensionNodeStatus otherNode)
    {
        // Arrange
        _nodeStatuses
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([otherNode]);

        // Act
        await _subject.RefreshNodeStatusAsync(2, CancellationToken.None).ConfigureAwait(false);

        // Assert
        _system.Verify(system => system.RequestRestartAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RefreshNodeStatusAsync_RequestsRestartAfterTheNodeRankedBefore_WhenThatNodeBecomesCurrent()
    {
        // Arrange
        _nodeStatuses
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ExtensionNodeStatus
                {
                    NodeId = "node-0",
                    AppliedGeneration = 1,
                    ExtensionHealth = ExtensionNodeHealth.StaleRequiresRestart,
                    LastSeenAt = new DateTime(2026, 10, 12, 11, 59, 30, DateTimeKind.Utc),
                    Message = "Extension changes require a restart."
                }
            ]);
        await _subject.RefreshNodeStatusAsync(2, CancellationToken.None).ConfigureAwait(false);
        var restartsWhileTheNodeBeforeIsStale = _system.Invocations.Count;
        _nodeStatuses
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ExtensionNodeStatus
                {
                    NodeId = "node-0",
                    AppliedGeneration = 2,
                    ExtensionHealth = ExtensionNodeHealth.CurrentHealthy,
                    SchedulerEligible = true,
                    LastSeenAt = new DateTime(2026, 10, 12, 11, 59, 45, DateTimeKind.Utc),
                    Message = "Extension activation succeeded."
                }
            ]);

        // Act
        await _subject.RefreshNodeStatusAsync(null, CancellationToken.None).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restartsWhileTheNodeBeforeIsStale, Is.Zero);
            _system.Verify(system => system.RequestRestartAsync(null, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task RefreshNodeStatusAsync_DoesNotRequestRestart_WhenAShutdownIsAlreadyRequested()
    {
        // Arrange
        _hostStatus.IsShutdownRequested = true;

        // Act
        await _subject.RefreshNodeStatusAsync(2, CancellationToken.None).ConfigureAwait(false);

        // Assert
        _system.Verify(
            system => system.RequestRestartAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RefreshNodeStatusAsync_DoesNotRequestRestart_WhenTheNodeAppliedTheCurrentGeneration()
    {
        // Arrange
        _generations.Setup(repository => repository.GetCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Act
        await _subject.RefreshNodeStatusAsync(null, CancellationToken.None).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _system.Verify(
                system => system.RequestRestartAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _nodeStatuses.Verify(
                repository => repository.UpsertAsync(
                    It.Is<ExtensionNodeStatus>(status =>
                        status.ExtensionHealth == ExtensionNodeHealth.CurrentHealthy &&
                        status.SchedulerEligible),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestCase(
        true,
        HostMode.Systemd,
        false,
        "Extension changes require a restart. Restart this node manually.")]
    [TestCase(
        true,
        HostMode.Default,
        false,
        "Extension changes require a restart. Restart this node manually.")]
    [TestCase(false, HostMode.Sentinel, true, "Extension changes require a restart.")]
    public async Task RefreshNodeStatusAsync_KeepsNodePendingRestart_WhenTheNodeCannotRestartItself(
        bool isClustered,
        HostMode mode,
        bool expectedSchedulerEligible,
        string expectedMessage)
    {
        // Arrange
        _hostStatus.Mode = mode;
        using var subject = new ExtensionActivationBackgroundService(
            _eventBus,
            _scopeFactory.Object,
            _hostEnvironment with { IsClustered = isClustered },
            _hostStatus,
            _timeProvider,
            NullLogger<ExtensionActivationBackgroundService>.Instance,
            _migrationState,
            _databaseFailures);

        // Act
        await subject.RefreshNodeStatusAsync(2, CancellationToken.None).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _system.Verify(
                system => system.RequestRestartAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _nodeStatuses.Verify(
                repository => repository.UpsertAsync(
                    It.Is<ExtensionNodeStatus>(status =>
                        status.ExtensionHealth == ExtensionNodeHealth.StaleRequiresRestart &&
                        status.SchedulerEligible == expectedSchedulerEligible &&
                        status.Message == expectedMessage),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task RefreshNodeStatusAsync_RecordsKnownSchema_WhenTheDatabaseHasNoUnknownMigrations()
    {
        // Act
        await _subject.RefreshNodeStatusAsync(1, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.That(_migrationState.IsSchemaKnown, Is.True);
    }

    [Test]
    public async Task RefreshNodeStatusAsync_StopsSchedulingAndRecordsMigrations_WhenTheDatabaseHasUnknownMigrations()
    {
        // Arrange
        _databaseProvider
            .Setup(provider => provider.GetUnknownMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["20991231000000_Future"]);

        // Act
        await _subject.RefreshNodeStatusAsync(1, CancellationToken.None).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_migrationState.IsSchemaKnown, Is.False);
            Assert.That(_migrationState.UnknownMigrations, Is.EqualTo(["20991231000000_Future"]));
            _nodeStatuses.Verify(
                repository => repository.UpsertAsync(
                    It.Is<ExtensionNodeStatus>(status =>
                        !status.SchedulerEligible &&
                        status.Message ==
                            "The database schema is newer than this node. Unknown migrations: 20991231000000_Future."),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_RechecksTheSchema_WhenADatabaseCommandFails()
    {
        // Arrange
        var firstCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _nodeStatuses
            .Setup(repository => repository.UpsertAsync(
                It.Is<ExtensionNodeStatus>(status => status.SchedulerEligible),
                It.IsAny<CancellationToken>()))
            .Callback(() => firstCheck.TrySetResult());
        _nodeStatuses
            .Setup(repository => repository.UpsertAsync(
                It.Is<ExtensionNodeStatus>(status => !status.SchedulerEligible),
                It.IsAny<CancellationToken>()))
            .Callback(() => recheck.TrySetResult());
        _generations.Setup(repository => repository.GetCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        await _subject.StartAsync(CancellationToken.None).ConfigureAwait(false);
        _migrationState.Complete();
        await firstCheck.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        _databaseProvider
            .Setup(provider => provider.GetUnknownMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["20991231000000_Future"]);

        // Act
        await _databaseFailures.CommandFailedAsync(null!, null!).ConfigureAwait(false);
        await recheck.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        Assert.That(_migrationState.IsSchemaKnown, Is.False);
    }

    [Test]
    public async Task ExecuteAsync_RechecksTheSchema_WhenADatabaseConnectionFails()
    {
        // Arrange
        var firstCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _nodeStatuses
            .Setup(repository => repository.UpsertAsync(
                It.Is<ExtensionNodeStatus>(status => status.SchedulerEligible),
                It.IsAny<CancellationToken>()))
            .Callback(() => firstCheck.TrySetResult());
        _nodeStatuses
            .Setup(repository => repository.UpsertAsync(
                It.Is<ExtensionNodeStatus>(status => !status.SchedulerEligible),
                It.IsAny<CancellationToken>()))
            .Callback(() => recheck.TrySetResult());
        _generations.Setup(repository => repository.GetCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        await _subject.StartAsync(CancellationToken.None).ConfigureAwait(false);
        _migrationState.Complete();
        await firstCheck.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        _databaseProvider
            .Setup(provider => provider.GetUnknownMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["20991231000000_Future"]);

        // Act
        await _databaseFailures.ConnectionFailedAsync(null!, null!).ConfigureAwait(false);
        await recheck.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        Assert.That(_migrationState.IsSchemaKnown, Is.False);
    }

    [Test]
    public async Task ExecuteAsync_RechecksTheSchema_WhenAnotherNodeMigratesTheDatabase()
    {
        // Arrange
        var firstCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _nodeStatuses
            .Setup(repository => repository.UpsertAsync(
                It.Is<ExtensionNodeStatus>(status => status.SchedulerEligible),
                It.IsAny<CancellationToken>()))
            .Callback(() => firstCheck.TrySetResult());
        _nodeStatuses
            .Setup(repository => repository.UpsertAsync(
                It.Is<ExtensionNodeStatus>(status => !status.SchedulerEligible),
                It.IsAny<CancellationToken>()))
            .Callback(() => recheck.TrySetResult());
        _generations.Setup(repository => repository.GetCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        await _subject.StartAsync(CancellationToken.None).ConfigureAwait(false);
        _migrationState.Complete();
        await firstCheck.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        _databaseProvider
            .Setup(provider => provider.GetUnknownMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["20991231000000_Future"]);

        // Act
        await _eventBus.PublishAsync(
            new MagpieEvent(
                Guid.Empty,
                MagpieEvents.Names.DatabaseMigrated,
                new Dictionary<string, string>(),
                default,
                default,
                "node-2")).ConfigureAwait(false);
        await recheck.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        Assert.That(_migrationState.IsSchemaKnown, Is.False);
    }

    [Test]
    public async Task RefreshNodeStatusAsync_DoesNotRequestRestart_WhenTheDatabaseHasUnknownMigrations()
    {
        // Arrange
        _databaseProvider
            .Setup(provider => provider.GetUnknownMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["20991231000000_Future"]);

        // Act
        await _subject.RefreshNodeStatusAsync(2, CancellationToken.None).ConfigureAwait(false);

        // Assert
        _system.Verify(
            system => system.RequestRestartAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
