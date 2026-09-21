using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs.Metadata;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Media;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Metadata;

[TestFixture]
public sealed class MediaInfoExtractScanJobTests
{
    private Mock<IMediaFileRepository> _mediaFileRepository = null!;
    private Mock<IJobManager> _jobManager = null!;
    private Mock<IProgressReporter> _progress = null!;
    private Mock<IMediaInfoExtractor> _extractor = null!;
    private List<JobEnqueueRequest> _queuedJobs = null!;
    private Guid _targetHierarchyItemId;
    private IJob _job = null!;
    private JobContext _scheduledContext = null!;
    private JobContext _manualContext = null!;
    private JobContext _eventContext = null!;
    private MagpieEvent _downloadImportedEvent = null!;

    [SetUp]
    public void SetUp()
    {
        _targetHierarchyItemId = Guid.NewGuid();
        _scheduledContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: Guid.NewGuid(),
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);
        _manualContext = _scheduledContext with { ScheduleTriggerId = null, ManualTriggerUserId = Guid.NewGuid() };
        _downloadImportedEvent = new MagpieEvent(
            Guid.NewGuid(), "download.imported",
            new Dictionary<string, string> { ["content.hierarchyItemId"] = _targetHierarchyItemId.ToString("D") },
            new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc), null);
        _eventContext = _scheduledContext with
        {
            ScheduleTriggerId = null,
            EventTriggerId = Guid.NewGuid(),
            Event = _downloadImportedEvent
        };
        _mediaFileRepository = new Mock<IMediaFileRepository> { CallBase = true };
        _mediaFileRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>());
        _progress = new Mock<IProgressReporter>();
        _extractor = new Mock<IMediaInfoExtractor>();
        _queuedJobs = [];
        _jobManager = new Mock<IJobManager>();
        _jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => _queuedJobs.Add(request))
            .ReturnsAsync(Guid.NewGuid());

        _job = new MediaInfoExtractScanJob(
            _mediaFileRepository.Object,
            _jobManager.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<MediaInfoExtractScanJob>.Instance,
            _progress.Object,
            _extractor.Object);
    }

    [Test]
    public void Metadata_HasExpectedShape_WhenRead()
    {
        // Arrange
        // Act
        var spec = MediaInfoExtractScanJob.Spec;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spec.JobType, Is.EqualTo("MediaInfoExtractScan"));
            Assert.That(spec.DisplayName, Is.EqualTo("Media info extract scan"));
            Assert.That(spec.Scope, Is.EqualTo(JobScope.Content));
            Assert.That(spec.OnDemand, Is.True);
            Assert.That(spec.ConcurrencyScope, Is.EqualTo(ConcurrencyScope.PerTypeAndTarget));
            Assert.That(spec.LockPartitionFields, Is.EqualTo(["contentHierarchyItemId"]));
            Assert.That(
                spec.LockPartitionEventContextKeys!["contentHierarchyItemId"],
                Is.EqualTo("content.hierarchyItemId"));
            Assert.That(
                spec.SupportedTriggerModes,
                Is.EquivalentTo([JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event]));
            Assert.That(spec.Parameters, Has.Count.EqualTo(2));
            Assert.That(spec.Parameters[0].Name, Is.EqualTo("contentHierarchyItemId"));
            Assert.That(spec.Parameters[0].Required, Is.False);
            Assert.That(spec.Parameters[1].Name, Is.EqualTo("maxAgeHours"));
            Assert.That(spec.Parameters[1].DefaultValue, Is.EqualTo("24"));
            Assert.That(spec.Timeout, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(spec.FailurePolicy, Is.EqualTo(JobFailurePolicy.Requeue));
        }
    }

    [Test]
    public async Task ExecuteAsync_QueuesMissingAndStaleMediaInfoTargets_WhenLibraryScanScheduled()
    {
        // Arrange
        var missingFileId = Guid.NewGuid();
        var staleFileId = Guid.NewGuid();
        _mediaFileRepository
            .Setup(r => r.QueryAsync(
                It.Is<MediaFileQuery>(query => query.MediaInfoStaleBefore != null),
                It.Is<PagingParameter>(paging => paging.PageSize == 500),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>
            {
                Items =
            [
                new MediaFile { Id = missingFileId, FilePath = "/media/movies/missing.mkv", FileKind = "video" },
                new MediaFile
                {
                    Id = staleFileId,
                    FilePath = "/media/movies/stale.mkv",
                    FileKind = "video",
                    MediaInfo = new MediaInfo()
                },
            ]
            });
        var userId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var jobContext = _scheduledContext with { ManualTriggerUserId = userId, CorrelationId = correlationId };

        // Act
        await _job.ExecuteAsync(jobContext, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_queuedJobs, Has.Count.EqualTo(2));
            Assert.That(_queuedJobs.Select(j => j.JobType), Is.All.EqualTo("MediaInfoExtractItem"));
            Assert.That(
                _queuedJobs.Select(j => j.Parameters!["mediaFileId"]),
                Is.EqualTo([missingFileId.ToString("D"), staleFileId.ToString("D")]));
            Assert.That(
                _queuedJobs.Select(j => j.Parameters!["filePath"]),
                Is.EqualTo(["/media/movies/missing.mkv", "/media/movies/stale.mkv"]));
            Assert.That(_queuedJobs[0].Parameters!["requireMissingMediaInfo"], Is.EqualTo("True"));
            Assert.That(_queuedJobs[1].Parameters!["requireMissingMediaInfo"], Is.EqualTo("False"));
            Assert.That(_queuedJobs.Select(j => j.Mode), Is.All.EqualTo(JobExecutionMode.Queue));
            Assert.That(_queuedJobs.Select(j => j.UserId), Is.All.EqualTo(userId));
            Assert.That(_queuedJobs.Select(j => j.CorrelationId), Is.All.EqualTo(correlationId));
        }
    }

    [Test]
    public async Task ExecuteAsync_UsesConfiguredAgeForCutoff_WhenMaxAgeHoursIsProvided()
    {
        // Arrange
        DateTime? capturedCutoff = null;
        _mediaFileRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback<MediaFileQuery, PagingParameter, SortParameter?, CancellationToken>(
                (query, _, _, _) => capturedCutoff = query.MediaInfoStaleBefore)
            .ReturnsAsync(new PagedList<MediaFile>
            {
                Items = [new MediaFile
            {
                Id = Guid.NewGuid(),
                FilePath = "/media/movies/stale.mkv",
                FileKind = "video",
                MediaInfo = new MediaInfo()
            }]
            });
        var jobContext = _scheduledContext with
        {
            Parameters = new Dictionary<string, string> { ["maxAgeHours"] = "7" }
        };

        // Act
        await _job.ExecuteAsync(jobContext, default).ConfigureAwait(false);

        // Assert
        Assert.That(capturedCutoff, Is.Not.Null);
        Assert.That(capturedCutoff!.Value, Is.EqualTo(new DateTime(2026, 5, 20, 5, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task ExecuteAsync_QueuesAtMostFiveHundredFiles_WhenThePageIsFull()
    {
        // Arrange
        var candidates = Enumerable.Range(0, 500)
            .Select(i => new MediaFile
            {
                Id = Guid.NewGuid(),
                FilePath = $"/media/movies/file-{i}.mkv",
                FileKind = "video"
            })
            .ToList();
        _mediaFileRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.Is<PagingParameter>(paging => paging.PageSize == 500),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { Items = candidates });
        var jobContext = _scheduledContext;

        // Act
        await _job.ExecuteAsync(jobContext, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_queuedJobs, Has.Count.EqualTo(500));
            _mediaFileRepository.Verify(
                r => r.QueryAsync(
                    It.Is<MediaFileQuery>(query => query.MediaInfoStaleBefore != null),
                    It.Is<PagingParameter>(paging => paging.PageSize == 500),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_ReportsNoCandidates_WhenNoFilesNeedExtract()
    {
        // Arrange
        _mediaFileRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>());
        var jobContext = _scheduledContext;

        // Act
        await _job.ExecuteAsync(jobContext, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_queuedJobs, Is.Empty);
            _progress.Verify(
                p => p.Report(1, "The media information scan completed because no file needs media information."),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotQueue_WhenExtractorUnavailable()
    {
        // Arrange
        var job = new MediaInfoExtractScanJob(
            _mediaFileRepository.Object,
            _jobManager.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<MediaInfoExtractScanJob>.Instance,
            _progress.Object);
        var jobContext = _scheduledContext;

        // Act
        await job.ExecuteAsync(jobContext, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_queuedJobs, Is.Empty);
            _progress.Verify(
                p => p.Report(1, "The scan stopped because no media information extractor is registered."),
                Times.Once);
            _mediaFileRepository.Verify(
                r => r.QueryAsync(
                    It.IsAny<MediaFileQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_RunsLibraryScan_WhenSystemTriggerWithoutTarget()
    {
        // Arrange
        _mediaFileRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.Is<PagingParameter>(paging => paging.PageSize == 500),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>
            {
                Items = [new MediaFile
            {
                Id = Guid.NewGuid(),
                FilePath = "/media/movies/system.mkv",
                FileKind = "video"
            }]
            });
        var jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        // Act
        await _job.ExecuteAsync(jobContext, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_queuedJobs, Has.Count.EqualTo(1));
            _mediaFileRepository.Verify(
                r => r.QueryAsync(
                    It.Is<MediaFileQuery>(query => query.MediaInfoStaleBefore != null),
                    It.Is<PagingParameter>(paging => paging.PageSize == 500),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public void ExecuteAsync_ThrowsAndDoesNotQueue_WhenManualTriggerWithoutTarget()
    {
        // Arrange
        var jobContext = _manualContext;

        // Act
        var ex = Assert.Throws<MagpieException>(() => _job.ExecuteAsync(jobContext).GetAwaiter().GetResult());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex, Has.Property(nameof(ex.Message)).Contain("scheduled or system trigger"));
            Assert.That(_queuedJobs, Is.Empty);
            _mediaFileRepository.Verify(
                r => r.QueryAsync(
                    It.IsAny<MediaFileQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_QueuesExtractPerFile_WhenTheEventSuppliesATarget()
    {
        // Arrange
        var targetFileId = Guid.NewGuid();
        _mediaFileRepository
            .Setup(r => r.QueryAsync(
                It.Is<MediaFileQuery>(query => query.ContentHierarchyItemId == _targetHierarchyItemId),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>
            {
                Items = [new MediaFile { Id = targetFileId, FileKind = "video", FilePath = "/media/event-target.mkv" }]
            });
        var jobContext = _eventContext;

        // Act
        await _job.ExecuteAsync(jobContext, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_queuedJobs, Has.Count.EqualTo(1));
            Assert.That(_queuedJobs[0].JobType, Is.EqualTo("MediaInfoExtractItem"));
            Assert.That(_queuedJobs[0].Parameters!["mediaFileId"], Is.EqualTo(targetFileId.ToString("D")));
            Assert.That(_queuedJobs[0].Parameters!["requireMissingMediaInfo"], Is.EqualTo("False"));
            _mediaFileRepository.Verify(
                r => r.QueryAsync(
                    It.Is<MediaFileQuery>(query => query.ContentHierarchyItemId == _targetHierarchyItemId),
                    It.IsAny<PagingParameter>(),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_UsesParameterItemId_WhenEventAlsoProvidesItemId()
    {
        // Arrange
        var eventHierarchyItemId = Guid.NewGuid();
        var jobContext = _eventContext with
        {
            Event = _downloadImportedEvent with
            {
                Context = new Dictionary<string, string>
                { ["content.hierarchyItemId"] = eventHierarchyItemId.ToString("D") }
            },
            Parameters = new Dictionary<string, string>
            { ["contentHierarchyItemId"] = _targetHierarchyItemId.ToString("D") }
        };

        // Act
        await _job.ExecuteAsync(jobContext, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _mediaFileRepository.Verify(
                r => r.QueryAsync(
                    It.Is<MediaFileQuery>(query => query.ContentHierarchyItemId == _targetHierarchyItemId),
                    It.IsAny<PagingParameter>(),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _mediaFileRepository.Verify(
                r => r.QueryAsync(
                    It.Is<MediaFileQuery>(query => query.ContentHierarchyItemId == eventHierarchyItemId),
                    It.IsAny<PagingParameter>(),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_QueuesExtractPerFile_WhenParametersSupplyATarget()
    {
        // Arrange
        var firstFileId = Guid.NewGuid();
        var secondFileId = Guid.NewGuid();
        _mediaFileRepository
            .Setup(r => r.QueryAsync(
                It.Is<MediaFileQuery>(query => query.ContentHierarchyItemId == _targetHierarchyItemId),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>
            {
                Items =
            [
                new MediaFile { Id = firstFileId, FileKind = "video", FilePath = "/media/target-a.mkv" },
                new MediaFile { Id = secondFileId, FileKind = "video", FilePath = "/media/target-b.mkv" },
            ]
            });
        var userId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var jobContext = _manualContext with
        {
            Parameters = new Dictionary<string, string>
            { ["contentHierarchyItemId"] = _targetHierarchyItemId.ToString() },
            ManualTriggerUserId = userId,
            CorrelationId = correlationId
        };

        // Act
        await _job.ExecuteAsync(jobContext, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_queuedJobs, Has.Count.EqualTo(2));
            Assert.That(_queuedJobs.Select(j => j.JobType), Is.All.EqualTo("MediaInfoExtractItem"));
            Assert.That(
                _queuedJobs.Select(j => j.Parameters!["mediaFileId"]),
                Is.EqualTo([firstFileId.ToString("D"), secondFileId.ToString("D")]));
            Assert.That(
                _queuedJobs.Select(j => j.Parameters!["filePath"]),
                Is.EqualTo(["/media/target-a.mkv", "/media/target-b.mkv"]));
            Assert.That(_queuedJobs.Select(j => j.Parameters!["requireMissingMediaInfo"]), Is.All.EqualTo("False"));
            Assert.That(_queuedJobs.Select(j => j.UserId), Is.All.EqualTo(userId));
            Assert.That(_queuedJobs.Select(j => j.CorrelationId), Is.All.EqualTo(correlationId));
        }
    }

    [Test]
    public async Task ExecuteAsync_ReportsNoFiles_WhenContentTargetHasNoFiles()
    {
        // Arrange
        var jobContext = _manualContext with
        {
            Parameters = new Dictionary<string, string>
            { ["contentHierarchyItemId"] = _targetHierarchyItemId.ToString() }
        };

        // Act
        await _job.ExecuteAsync(jobContext, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_queuedJobs, Is.Empty);
            _progress.Verify(
                p => p.Report(1, "The media information scan completed because no file needs media information."),
                Times.Once);
        }
    }

    [Test]
    public void ExecuteAsync_Throws_WhenContentHierarchyItemIdNotGuid()
    {
        // Arrange
        var jobContext = _manualContext with
        {
            Parameters = new Dictionary<string, string> { ["contentHierarchyItemId"] = "not-a-guid" }
        };

        // Act
        var ex = Assert.Throws<ArgumentException>(() => _job.ExecuteAsync(jobContext).GetAwaiter().GetResult());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ex,
                Has.Property(nameof(ex.Message)).Contain("The content hierarchy item ID has an invalid format."));
            Assert.That(_queuedJobs, Is.Empty);
        }
    }

    [Test]
    public void ExecuteAsync_Throws_WhenEventContentHierarchyItemIdNotGuid()
    {
        // Arrange
        var jobContext = _eventContext with
        {
            Event = _downloadImportedEvent with
            {
                Context = new Dictionary<string, string> { ["content.hierarchyItemId"] = "not-a-guid" }
            }
        };

        // Act
        var ex = Assert.Throws<ArgumentException>(() => _job.ExecuteAsync(jobContext).GetAwaiter().GetResult());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ex,
                Has.Property(nameof(ex.Message)).Contain("The job received an invalid content hierarchy item ID."));
            Assert.That(_queuedJobs, Is.Empty);
        }
    }
}
