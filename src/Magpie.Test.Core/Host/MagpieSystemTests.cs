using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Models;
using Magpie.Models.Data;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class MagpieSystemTests
{
    private HostStatusInfo _hostStatus = null!;
    private Mock<IHostApplicationLifetime> _lifetime = null!;
    private Mock<IAuditRecordRepository> _auditRecordRepository = null!;
    private FakeTimeProvider _timeProvider = null!;
    private MagpieHostEnvironment _hostEnvironment = null!;
    private MagpieSystem _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _hostStatus = new HostStatusInfo();
        _lifetime = new Mock<IHostApplicationLifetime>();
        _auditRecordRepository = new Mock<IAuditRecordRepository>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero));
        _hostEnvironment = new MagpieHostEnvironment(
            NodeId: "node-1",
            IsClustered: false,
            IsSupervised: true,
            SystemRoot: "/tmp/magpie",
            ExtensionsRoot: "/tmp/magpie/extensions",
            ConfigRoot: "/tmp/magpie/config",
            DataRoot: "/tmp/magpie/data",
            StartedAt: new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));
        _subject = new MagpieSystem(
            _hostStatus,
            _hostEnvironment,
            _lifetime.Object,
            _auditRecordRepository.Object,
            _timeProvider,
            NullLogger<MagpieSystem>.Instance);
    }

    [TestCase(HostMode.Sentinel)]
    [TestCase(HostMode.WindowsService)]
    public async Task RequestRestartAsync_SetsShutdownExitCodeToRestart_WhenModeReadsIt(HostMode mode)
    {
        // Arrange
        _hostStatus.Mode = mode;

        // Act
        await _subject.RequestRestartAsync(null, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.That(_hostStatus.ShutdownExitCode, Is.EqualTo(9));
    }

    [Test]
    public async Task RequestShutdownAsync_ReportsCompleteRequest_WhenAnotherRequestIsPublishingMetadata()
    {
        // Arrange
        var readingTime = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTime = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.SetupSequence(provider => provider.GetUtcNow())
            .Returns(() =>
            {
                readingTime.SetResult();
                releaseTime.Task.Wait(TimeSpan.FromSeconds(30));
                return new DateTimeOffset(2024, 5, 6, 12, 0, 0, TimeSpan.Zero);
            })
            .Returns(new DateTimeOffset(2024, 5, 6, 12, 0, 0, TimeSpan.Zero));
        var subject = new MagpieSystem(
            _hostStatus,
            _hostEnvironment,
            _lifetime.Object,
            _auditRecordRepository.Object,
            timeProvider.Object,
            NullLogger<MagpieSystem>.Instance);

        // Act
        var first = Task.Run(() => subject.RequestShutdownAsync(null));
        await readingTime.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        var second = Task.Run(
            () => Assert.ThrowsAsync<MagpieShutdownException>(() => subject.RequestShutdownAsync(null)));
        releaseTime.SetResult();
        await first.ConfigureAwait(false);
        var error = await second.ConfigureAwait(false);

        // Assert
        Assert.That(error!.RequestedAtUtc, Is.EqualTo(new DateTime(2024, 5, 6, 12, 0, 0, DateTimeKind.Utc)));
    }

    [TestCase(HostMode.Sentinel)]
    [TestCase(HostMode.WindowsService)]
    public async Task RequestRestartAsync_SetsShutdownExitCodeBeforeStoppingTheApplication_WhenModeReadsIt(
        HostMode mode)
    {
        // Arrange
        _hostStatus.Mode = mode;
        int? observedExitCodeDuringStop = null;
        _lifetime.Setup(applicationLifetime => applicationLifetime.StopApplication())
            .Callback(() => observedExitCodeDuringStop = _hostStatus.ShutdownExitCode);

        // Act
        await _subject.RequestRestartAsync(null, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.That(observedExitCodeDuringStop, Is.EqualTo(9));
    }

    [TestCase(HostMode.Sentinel)]
    [TestCase(HostMode.WindowsService)]
    public async Task RequestShutdownAsync_SetsShutdownExitCodeToShutdown_WhenModeReadsIt(HostMode mode)
    {
        // Arrange
        _hostStatus.Mode = mode;

        // Act
        await _subject.RequestShutdownAsync(null, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.That(_hostStatus.ShutdownExitCode, Is.EqualTo(8));
    }

    [Test]
    public async Task RequestRestartAsync_LeavesShutdownExitCodeNull_WhenSystemd()
    {
        // Arrange
        _hostStatus.Mode = HostMode.Systemd;

        // Act
        await _subject.RequestRestartAsync(null, CancellationToken.None).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_hostStatus.ShutdownExitCode, Is.Null);
            _lifetime.Verify(applicationLifetime => applicationLifetime.StopApplication(), Times.Once);
        }
    }

    [Test]
    public async Task RequestRestartAsync_WritesTheAuditRecordBeforeStoppingTheApplication_WhenSupervised()
    {
        // Arrange
        var auditWrittenBeforeStop = false;
        var audited = false;
        _auditRecordRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => audited = true)
            .ReturnsAsync(new AuditRecord { Category = "System", Operation = "Restart" });
        _lifetime.Setup(applicationLifetime => applicationLifetime.StopApplication())
            .Callback(() => auditWrittenBeforeStop = audited);

        // Act
        await _subject.RequestRestartAsync(null, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.That(auditWrittenBeforeStop, Is.True);
    }

    [Test]
    public async Task RequestRestartAsync_StopsTheApplication_WhenTheAuditRecordCannotBeWritten()
    {
        // Arrange
        _auditRecordRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("offline"));

        // Act
        await _subject.RequestRestartAsync(null, CancellationToken.None).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _lifetime.Verify(applicationLifetime => applicationLifetime.StopApplication(), Times.Once);
            Assert.That(_hostStatus.IsShutdownRequested, Is.True);
        }
    }

    [Test]
    public async Task RequestShutdownAsync_Throws_WhenAnotherScopeAlreadyRequestedTheShutdown()
    {
        // Arrange
        await _subject.RequestShutdownAsync(null, CancellationToken.None).ConfigureAwait(false);
        var secondScope = new MagpieSystem(
            _hostStatus,
            _hostEnvironment,
            _lifetime.Object,
            _auditRecordRepository.Object,
            _timeProvider,
            NullLogger<MagpieSystem>.Instance);

        // Act
        // Assert
        Assert.That(
            async () => await secondScope.RequestShutdownAsync(null, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<MagpieShutdownException>().With.Message.EqualTo("A shutdown is already in progress."));
    }

    [Test]
    public void RequestRestartAsync_Throws_WhenHostIsNotSupervised()
    {
        // Arrange
        _hostStatus.Mode = HostMode.Default;
        var subject = new MagpieSystem(
            _hostStatus,
            _hostEnvironment with { IsSupervised = false },
            _lifetime.Object,
            _auditRecordRepository.Object,
            _timeProvider,
            NullLogger<MagpieSystem>.Instance);

        // Act
        // Assert
        Assert.That(
            async () => await subject.RequestRestartAsync(null, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<MagpieShutdownException>().With.Message.EqualTo("The host is not supervised."));
    }
}
