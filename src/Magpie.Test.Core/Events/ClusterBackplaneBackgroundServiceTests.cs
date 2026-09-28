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
}
