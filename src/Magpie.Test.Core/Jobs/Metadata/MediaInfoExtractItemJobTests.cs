using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs.Metadata;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Media;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Metadata;

[TestFixture]
public class MediaInfoExtractItemJobTests
{
    private Mock<IMediaFileRepository> _mediaFileRepository = null!;
    private Mock<IFileSystem> _fileSystem = null!;
    private Mock<IMediaInfoExtractor> _extractor = null!;
    private Mock<IProgressReporter> _progress = null!;
    private IJob _job = null!;

    [SetUp]
    public void SetUp()
    {
        _mediaFileRepository = new Mock<IMediaFileRepository>();
        _mediaFileRepository
            .Setup(r => r.GetByIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new MediaFile
            {
                Id = id,
                ContentItemId = Guid.NewGuid(),
                FileKind = "video",
                FilePath = "/media/movies/target.mkv"
            });
        _mediaFileRepository
            .Setup(r => r.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _fileSystem = new Mock<IFileSystem>();
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
        _fileSystem.Setup(f => f.GetFileSize(It.IsAny<string>())).Returns(1024);

        _fileSystem
            .Setup(f => f.LookupMimeTypeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("video/x-matroska");
        _fileSystem
            .Setup(f => f.ResolveFileKind("video/x-matroska"))
            .Returns(FileKind.Video);

        _extractor = new Mock<IMediaInfoExtractor>();
        _extractor
            .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaInfo { Container = "matroska" });

        _progress = new Mock<IProgressReporter>();

        _job = new MediaInfoExtractItemJob(
            _mediaFileRepository.Object,
            _fileSystem.Object,
            TimeProvider.System,
            NullLogger<MediaInfoExtractItemJob>.Instance,
            _progress.Object,
            _extractor.Object);
    }

    [Test]
    public void MediaInfoExtractItemJob_JobMetadata_HasExpectedShape()
    {
        // Arrange
        // Act
        var spec = MediaInfoExtractItemJob.Spec;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spec.JobType, Is.EqualTo("MediaInfoExtractItem"));
            Assert.That(spec.Scope, Is.EqualTo(JobScope.Administrative));
            Assert.That(spec.OnDemand, Is.False);
            Assert.That(spec.ConcurrencyScope, Is.EqualTo(ConcurrencyScope.PerTarget));
            Assert.That(spec.LockPartitionFields, Is.EqualTo(["mediaFileId"]));
            Assert.That(spec.Parameters, Has.Count.EqualTo(3));
            Assert.That(spec.Parameters[0].Name, Is.EqualTo("mediaFileId"));
            Assert.That(spec.Parameters[0].Required, Is.True);
            Assert.That(spec.Parameters[1].Name, Is.EqualTo("filePath"));
            Assert.That(spec.Parameters[1].Required, Is.True);
            Assert.That(spec.Parameters[2].Name, Is.EqualTo("requireMissingMediaInfo"));
            Assert.That(spec.Parameters[2].Required, Is.False);
            Assert.That(spec.Timeout, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(spec.FailurePolicy, Is.EqualTo(JobFailurePolicy.Retry));
        }
    }

    [Test]
    public async Task ExecuteAsync_PersistsMediaFileDetails_WhenExtractorReturnsMediaInfo()
    {
        // Arrange
        var mediaFileId = Guid.NewGuid();
        var jobContext = new JobContext(
            Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["mediaFileId"] = mediaFileId.ToString("D"),
                ["filePath"] = "/media/movies/target.mkv",
                ["requireMissingMediaInfo"] = "false"
            },
            Event: null);

        // Act
        await _job.ExecuteAsync(jobContext, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _extractor.Verify(
                e => e.ExtractAsync("/media/movies/target.mkv", It.IsAny<CancellationToken>()),
                Times.Once);
            _mediaFileRepository.Verify(
                r => r.UpdateAsync(
                    It.Is<MediaFile>(file =>
                        file.Id == mediaFileId &&
                        file.MimeType == "video/x-matroska" &&
                        file.FileSize == 1024 &&
                        file.MediaInfo!.Container == "matroska"),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _progress.Verify(p => p.Report(1, "The media information refresh completed."), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_DefaultsRequireMissingMediaInfoTrue_WhenParameterOmitted()
    {
        // Arrange
        var mediaFileId = Guid.NewGuid();
        var jobContext = new JobContext(
            Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["mediaFileId"] = mediaFileId.ToString("D"),
                ["filePath"] = "/media/movies/target.mkv"
            },
            Event: null);

        // Act
        await _job.ExecuteAsync(jobContext, default).ConfigureAwait(false);

        // Assert
        _mediaFileRepository.Verify(
            r => r.UpdateAsync(
                It.Is<MediaFile>(file => file.Id == mediaFileId && file.MediaInfo != null),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SkipsDetailWrite_WhenRequireMissingMediaInfoAndAlreadyPopulated()
    {
        // Arrange
        var mediaFileId = Guid.NewGuid();
        _mediaFileRepository
            .Setup(r => r.GetByIdAsync(
                mediaFileId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaFile
            {
                Id = mediaFileId,
                ContentItemId = Guid.NewGuid(),
                FileKind = "video",
                FilePath = "/media/movies/target.mkv",
                MediaInfo = new MediaInfo()
            });
        var jobContext = new JobContext(
            Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["mediaFileId"] = mediaFileId.ToString("D"),
                ["filePath"] = "/media/movies/target.mkv"
            },
            Event: null);

        // Act
        await _job.ExecuteAsync(jobContext, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _mediaFileRepository.Verify(
                r => r.UpdateAsync(
                    It.IsAny<MediaFile>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _progress.Verify(
                p => p.Report(1, "The media information refresh completed and did not change the media file."),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_ReportsUnchanged_WhenFileMissingOnDisk()
    {
        // Arrange
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);
        var jobContext = new JobContext(
            Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["mediaFileId"] = Guid.NewGuid().ToString("D"),
                ["filePath"] = "/media/movies/gone.mkv"
            },
            Event: null);

        // Act
        await _job.ExecuteAsync(jobContext, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _extractor.Verify(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _progress.Verify(
                p => p.Report(1, "The media information refresh completed and did not change the media file."),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_ReportsSkipped_WhenExtractorUnavailable()
    {
        // Arrange
        var job = new MediaInfoExtractItemJob(
            _mediaFileRepository.Object,
            _fileSystem.Object,
            TimeProvider.System,
            NullLogger<MediaInfoExtractItemJob>.Instance,
            _progress.Object);
        var jobContext = new JobContext(
            Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["mediaFileId"] = Guid.NewGuid().ToString("D"),
                ["filePath"] = "/media/movies/target.mkv"
            },
            Event: null);

        // Act
        await job.ExecuteAsync(jobContext, default).ConfigureAwait(false);

        // Assert
        _progress.Verify(
            p => p.Report(1, "The refresh stopped because no media information extractor is registered."),
            Times.Once);
    }

    [Test]
    public void ExecuteAsync_Throws_WhenMediaFileIdMissing()
    {
        // Arrange
        var jobContext = new JobContext(
            Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["filePath"] = "/media/movies/target.mkv"
            },
            Event: null);

        // Act
        var ex = Assert.Throws<ArgumentException>(() => _job.ExecuteAsync(jobContext).GetAwaiter().GetResult());

        // Assert
        Assert.That(ex, Has.Property(nameof(ex.Message)).Contain("mediaFileId"));
    }

    [Test]
    public void ExecuteAsync_Throws_WhenFilePathMissing()
    {
        // Arrange
        var jobContext = new JobContext(
            Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["mediaFileId"] = Guid.NewGuid().ToString("D")
            },
            Event: null);

        // Act
        var ex = Assert.Throws<ArgumentException>(() => _job.ExecuteAsync(jobContext).GetAwaiter().GetResult());

        // Assert
        Assert.That(ex, Has.Property(nameof(ex.Message)).Contain("filePath"));
    }
}
