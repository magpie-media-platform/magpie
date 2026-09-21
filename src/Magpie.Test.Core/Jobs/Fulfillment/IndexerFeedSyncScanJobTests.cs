using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class IndexerFeedSyncScanJobTests
{
    private readonly IProgressReporter _progressReporter = Mock.Of<IProgressReporter>();

    private Mock<IIndexerFeedRepository> _indexerFeedRepository = null!;
    private Mock<IJobManager> _jobManager = null!;
    private List<IndexerFeed> _feeds = null!;
    private List<JobEnqueueRequest> _enqueued = null!;
    private JobContext _jobContext = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _userId = Guid.NewGuid();
        _feeds = [];
        _enqueued = [];

        _indexerFeedRepository = new Mock<IIndexerFeedRepository>();
        _indexerFeedRepository
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _feeds);

        _jobManager = new Mock<IJobManager>();
        _jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => _enqueued.Add(request))
            .ReturnsAsync(Guid.NewGuid());

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
    public async Task ExecuteAsync_EnqueuesOneItemPerFeed_WhenFeedsAreEnabled()
    {
        // Arrange
        var first = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            Name = "A-Feed",
            ProviderType = "fake",
            Enabled = true,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        var second = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            Name = "B-Feed",
            ProviderType = "fake",
            Enabled = true,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _feeds.Add(first);
        _feeds.Add(second);
        var service = new IndexerFeedSyncScanJob(
            _indexerFeedRepository.Object,
            _jobManager.Object,
            NullLogger<IndexerFeedSyncScanJob>.Instance,
            _progressReporter);

        // Act
        await service.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_enqueued, Has.Count.EqualTo(2));
            Assert.That(_enqueued.Select(r => r.JobType), Is.All.EqualTo("IndexerFeedSyncItem"));
            Assert.That(_enqueued.Select(r => r.Mode), Is.All.EqualTo(JobExecutionMode.Queue));
            Assert.That(_enqueued.Select(r => r.UserId), Is.All.EqualTo(_userId));
            Assert.That(
                _enqueued.Select(r => r.Parameters!["feedId"]),
                Is.EquivalentTo([first.Id.ToString(), second.Id.ToString()]));
        }
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesNothing_WhenNoFeedsExist()
    {
        // Arrange
        var service = new IndexerFeedSyncScanJob(
            _indexerFeedRepository.Object,
            _jobManager.Object,
            NullLogger<IndexerFeedSyncScanJob>.Instance,
            _progressReporter);

        // Act
        await service.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_SkipsFeed_WhenFeedIsDisabled()
    {
        // Arrange
        var enabled = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            Name = "Enabled",
            ProviderType = "fake",
            Enabled = true,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        var disabled = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            Name = "Disabled",
            ProviderType = "fake",
            Enabled = false,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _feeds.Add(enabled);
        _feeds.Add(disabled);
        var service = new IndexerFeedSyncScanJob(
            _indexerFeedRepository.Object,
            _jobManager.Object,
            NullLogger<IndexerFeedSyncScanJob>.Instance,
            _progressReporter);

        // Act
        await service.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_enqueued, Has.Count.EqualTo(1));
            Assert.That(
                _enqueued[0].Parameters!["feedId"],
                Is.EqualTo(enabled.Id.ToString()));
        }
    }

    [Test]
    public async Task ExecuteAsync_ContinuesEnqueuing_WhenOneEnqueueThrows()
    {
        // Arrange
        var failing = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            Name = "A-Failing",
            ProviderType = "fake",
            Enabled = true,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        var working = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            Name = "B-Working",
            ProviderType = "fake",
            Enabled = true,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _feeds.Add(failing);
        _feeds.Add(working);
        _jobManager
            .Setup(m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.Parameters!["feedId"] == failing.Id.ToString()),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Enqueue failed"));
        var service = new IndexerFeedSyncScanJob(
            _indexerFeedRepository.Object,
            _jobManager.Object,
            NullLogger<IndexerFeedSyncScanJob>.Instance,
            _progressReporter);

        // Act
        await service.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.Parameters!["feedId"] == working.Id.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_PropagatesCancellation_WhenCancellationPrecedesEnqueue()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            Name = "Feed",
            ProviderType = "fake",
            Enabled = true,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _feeds.Add(feed);
        var service = new IndexerFeedSyncScanJob(
            _indexerFeedRepository.Object,
            _jobManager.Object,
            NullLogger<IndexerFeedSyncScanJob>.Instance,
            _progressReporter);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        // Act
        var ex = Assert.ThrowsAsync<OperationCanceledException>(() => service.ExecuteAsync(_jobContext, cts.Token));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex, Is.Not.Null);
            _jobManager.Verify(
                m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
