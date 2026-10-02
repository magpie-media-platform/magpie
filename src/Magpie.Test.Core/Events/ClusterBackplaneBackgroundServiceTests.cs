using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
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

    private static readonly Guid EventId = new("00000000-0000-0000-0000-000000000001");

    private static readonly Guid MarkerEventId = new("00000000-0000-0000-0000-000000000002");

    private TaskCompletionSource _broadcastMarker = null!;
    private TaskCompletionSource _publishMarker = null!;
    private Subject<MagpieEvent> _peerEvents = null!;
    private Mock<IClusterBackplane> _backplane = null!;
    private Mock<IObserver<MagpieEvent>> _observer = null!;
    private EventBus _eventBus = null!;
    private ClusterBackplaneBackgroundService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _broadcastMarker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _publishMarker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _peerEvents = new();
        var migrationState = new DatabaseMigrationState();
        migrationState.Complete();
        var hostEnvironment = new MagpieHostEnvironment(
            LocalNodeId,
            IsClustered: true,
            IsSupervised: false,
            SystemRoot: string.Empty,
            ExtensionsRoot: string.Empty,
            ConfigRoot: string.Empty,
            DataRoot: string.Empty,
            StartedAt: DateTime.UtcNow);
        _eventBus = new EventBus(hostEnvironment, Mock.Of<IJobExecutionScopeFactory>(), TimeProvider.System);
        _backplane = new Mock<IClusterBackplane>();
        _backplane
            .Setup(backplane => backplane.CreateListener())
            .Returns(() => new EventListener(_peerEvents, null));
        _backplane
            .Setup(backplane => backplane.BroadcastEventAsync(
                It.Is<MagpieEvent>(magpieEvent => magpieEvent.Id == MarkerEventId),
                It.IsAny<CancellationToken>()))
            .Callback(() => _broadcastMarker.TrySetResult());
        _observer = new Mock<IObserver<MagpieEvent>>();
        _observer
            .Setup(observer => observer.OnNext(It.Is<MagpieEvent>(magpieEvent => magpieEvent.Id == MarkerEventId)))
            .Callback(() => _publishMarker.TrySetResult());
        _subject = new ClusterBackplaneBackgroundService(
            _eventBus,
            hostEnvironment,
            NullLogger<ClusterBackplaneBackgroundService>.Instance,
            backplane: _backplane.Object,
            databaseMigration: migrationState);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _subject.StopAsync(CancellationToken.None).ConfigureAwait(false);
        _subject.Dispose();
        _peerEvents.Dispose();
        _eventBus.Dispose();
    }

    [Test]
    public async Task ExecuteAsync_BroadcastsEvent_WhenOriginIsLocalNode()
    {
        // Arrange
        await _subject.StartAsync(CancellationToken.None).ConfigureAwait(false);

        // Act
        await _eventBus.PublishAsync(new MagpieEvent(
            EventId,
            "test.event",
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            LocalNodeId)).ConfigureAwait(false);
        await _eventBus.PublishAsync(new MagpieEvent(
            MarkerEventId,
            "test.event",
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            LocalNodeId)).ConfigureAwait(false);
        await _broadcastMarker.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        _backplane.Verify(
            backplane => backplane.BroadcastEventAsync(
                It.Is<MagpieEvent>(magpieEvent => magpieEvent.Id == EventId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SkipsBroadcast_WhenOriginIsPeerNode()
    {
        // Arrange
        await _subject.StartAsync(CancellationToken.None).ConfigureAwait(false);

        // Act
        await _eventBus.PublishAsync(new MagpieEvent(
            EventId,
            "test.event",
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            "node-B")).ConfigureAwait(false);
        await _eventBus.PublishAsync(new MagpieEvent(
            MarkerEventId,
            "test.event",
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            LocalNodeId)).ConfigureAwait(false);
        await _broadcastMarker.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        _backplane.Verify(
            backplane => backplane.BroadcastEventAsync(
                It.Is<MagpieEvent>(magpieEvent => magpieEvent.Id == EventId),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_PublishesEvent_WhenOriginIsPeerNode()
    {
        // Arrange
        using var subscription = _eventBus.Events.Subscribe(_observer.Object);
        await _subject.StartAsync(CancellationToken.None).ConfigureAwait(false);

        // Act
        _peerEvents.OnNext(new MagpieEvent(
            EventId,
            "test.event",
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            "node-B"));
        _peerEvents.OnNext(new MagpieEvent(
            MarkerEventId,
            "test.event",
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            "node-B"));
        await _publishMarker.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        _observer.Verify(
            observer => observer.OnNext(It.Is<MagpieEvent>(magpieEvent => magpieEvent.Id == EventId)),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SkipsPublish_WhenOriginIsLocalNode()
    {
        // Arrange
        using var subscription = _eventBus.Events.Subscribe(_observer.Object);
        await _subject.StartAsync(CancellationToken.None).ConfigureAwait(false);

        // Act
        _peerEvents.OnNext(new MagpieEvent(
            EventId,
            "test.event",
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            LocalNodeId));
        _peerEvents.OnNext(new MagpieEvent(
            MarkerEventId,
            "test.event",
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            "node-B"));
        await _publishMarker.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        _observer.Verify(
            observer => observer.OnNext(It.Is<MagpieEvent>(magpieEvent => magpieEvent.Id == EventId)),
            Times.Never);
    }
}
