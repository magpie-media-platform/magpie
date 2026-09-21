using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Magpie.Core.Events;
using Magpie.Models;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Events;

[TestFixture]
public sealed class ClusterBackplaneBackgroundServiceTests
{
    private const string LocalNodeId = "node-A";

    private Mock<IClusterBackplane> _backplane = null!;
    private Mock<IEventBus> _eventBus = null!;
    private ClusterBackplaneBackgroundService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        var migrationState = new DatabaseMigrationState();
        migrationState.Complete();
        _backplane = new Mock<IClusterBackplane>();
        _eventBus = new Mock<IEventBus>();
        _subject = new ClusterBackplaneBackgroundService(
            _eventBus.Object,
            new MagpieHostEnvironment(
                LocalNodeId,
                IsClustered: true,
                IsSupervised: false,
                SystemRoot: string.Empty,
                ExtensionsRoot: string.Empty,
                ConfigRoot: string.Empty,
                DataRoot: string.Empty,
                StartedAt: DateTime.UtcNow),
            NullLogger<ClusterBackplaneBackgroundService>.Instance,
            backplane: _backplane.Object,
            databaseMigration: migrationState);
    }

    [TearDown]
    public void TearDown()
    {
        _subject.Dispose();
    }

    [Test]
    public async Task RelayOutboundAsync_BroadcastsEvent_WhenOriginIsLocalNode()
    {
        // Arrange
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "test.event",
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            LocalNodeId);

        // Act
        await _subject.RelayOutboundAsync(magpieEvent, default).ConfigureAwait(false);

        // Assert
        _backplane.Verify(b => b.BroadcastEventAsync(magpieEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RelayOutboundAsync_SkipsEvent_WhenOriginIsPeerNode()
    {
        // Arrange
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "test.event",
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            "node-B");

        // Act
        await _subject.RelayOutboundAsync(magpieEvent, default).ConfigureAwait(false);

        // Assert
        _backplane.Verify(
            b => b.BroadcastEventAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RelayInboundAsync_PublishesEvent_WhenOriginIsPeerNode()
    {
        // Arrange
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "test.event",
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            "node-B");

        // Act
        await _subject.RelayInboundAsync(magpieEvent, default).ConfigureAwait(false);

        // Assert
        _eventBus.Verify(b => b.PublishAsync(magpieEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RelayInboundAsync_SkipsEvent_WhenOriginIsLocalNode()
    {
        // Arrange
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "test.event",
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            LocalNodeId);

        // Act
        await _subject.RelayInboundAsync(magpieEvent, default).ConfigureAwait(false);

        // Assert
        _eventBus.Verify(b => b.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_SkipsBothRelayLoops_WhenTheHostIsNotClustered()
    {
        // Arrange
        var migration = new DatabaseMigrationState();
        migration.Complete();
        using var subject = new ClusterBackplaneBackgroundService(
            _eventBus.Object,
            new MagpieHostEnvironment(
                LocalNodeId,
                IsClustered: false,
                IsSupervised: false,
                SystemRoot: string.Empty,
                ExtensionsRoot: string.Empty,
                ConfigRoot: string.Empty,
                DataRoot: string.Empty,
                StartedAt: new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc)),
            NullLogger<ClusterBackplaneBackgroundService>.Instance,
            databaseMigration: migration,
            backplane: _backplane.Object);

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _backplane.Verify(value => value.CreateListener(), Times.Never);
            _eventBus.Verify(value => value.CreateListener(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_RelaysBufferedEvent_WhenDatabaseStartupCompletes()
    {
        // Arrange
        var migration = new DatabaseMigrationState();
        using var events = new Subject<MagpieEvent>();
        using var listener = new EventListener(events, null);
        _eventBus.Setup(value => value.CreateListener(null, false)).Returns(listener);
        var peerEvents = Channel.CreateUnbounded<MagpieEvent>();
        var peerListener = new Mock<IEventListener>();
        peerListener.Setup(value => value.ReadAllAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) => peerEvents.Reader.ReadAllAsync(token));
        _backplane.Setup(value => value.CreateListener()).Returns(peerListener.Object);
        var relayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _backplane.Setup(value => value.BroadcastEventAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => relayed.TrySetResult()).Returns(Task.CompletedTask);
        using var subject = new ClusterBackplaneBackgroundService(
            _eventBus.Object,
            new MagpieHostEnvironment(
                LocalNodeId,
                IsClustered: true,
                IsSupervised: false,
                SystemRoot: string.Empty,
                ExtensionsRoot: string.Empty,
                ConfigRoot: string.Empty,
                DataRoot: string.Empty,
                StartedAt: new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc)),
            NullLogger<ClusterBackplaneBackgroundService>.Instance,
            databaseMigration: migration,
            backplane: _backplane.Object);
        var magpieEvent = new MagpieEvent(Guid.NewGuid(), "extension.disabled", new Dictionary<string, string>(),
            DateTime.UtcNow, DateTime.UtcNow, "node-A");

        // Act
        await subject.StartAsync(default).ConfigureAwait(false);
        events.OnNext(magpieEvent);
        migration.Complete();
        await relayed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await subject.StopAsync(default).ConfigureAwait(false);

        // Assert
        _backplane.Verify(value => value.BroadcastEventAsync(magpieEvent, It.IsAny<CancellationToken>()), Times.Once);
    }
}
