using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Content;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Jobs;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class MediaFileRenameJobTests
{
    private Mock<IMediaFileRepository> _mediaFiles = null!;
    private Mock<IContentRepository> _contentItems = null!;
    private Mock<IContentFileService> _contentFileService = null!;
    private Mock<IProgressReporter> _progress = null!;
    private IJob _subject = null!;
    private Guid _mediaFileId;
    private Guid _contentHierarchyItemId;
    private Guid _userId;
    private Guid _correlationId;
    private MediaFile _mediaFile = null!;
    private ContentRename _preview = null!;
    private JobContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _mediaFileId = Guid.NewGuid();
        _contentHierarchyItemId = Guid.NewGuid();
        _userId = Guid.NewGuid();
        _correlationId = Guid.NewGuid();
        _mediaFile = new MediaFile
        {
            Id = _mediaFileId,
            FilePath = Path.Combine(Path.GetTempPath(), "rename-job", "input.mkv"),
            ContentItemId = Guid.NewGuid(),
            FileKind = "Video"
        };
        _preview = new ContentRename
        {
            Original = new FileInfo(_mediaFile.FilePath),
            Rename = new FileInfo(Path.Combine(Path.GetTempPath(), "rename-job", "Movie", "input.mkv")),
            Item = new ContentHierarchyItem
            {
                Id = _contentHierarchyItemId,
                ContentType = "movie",
                Level = "movie",
                Slug = "movie",
                Name = "Movie",
                CreatedAt = DateTime.UtcNow
            }
        };
        _mediaFiles = new Mock<IMediaFileRepository>();
        _mediaFiles
            .Setup(r => r.GetByIdAsync(_mediaFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mediaFile);
        _contentItems = new Mock<IContentRepository>();
        _contentItems
            .Setup(r => r.GetByIdAsync(_mediaFile.ContentItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentItem
            {
                Id = _mediaFile.ContentItemId,
                ContentHierarchyItemId = _contentHierarchyItemId
            });
        _contentFileService = new Mock<IContentFileService>();
        _contentFileService
            .Setup(e => e.PreviewRenameAsync(
                _contentHierarchyItemId,
                It.IsAny<FileInfo>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_preview);
        _contentFileService
            .Setup(e => e.ExecuteRenameAsync(_preview, _userId, _correlationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaFile
            {
                Id = _mediaFileId,
                ContentItemId = _mediaFile.ContentItemId,
                FileKind = "Video",
                FilePath = _preview.Rename.FullName
            });
        _progress = new Mock<IProgressReporter>();
        _subject = new MediaFileRenameJob(
            _mediaFiles.Object,
            _contentItems.Object,
            _contentFileService.Object,
            _progress.Object);
        _context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["mediaFileId"] = _mediaFileId.ToString("D")
            },
            Event: null,
            CorrelationId: _correlationId);
    }

    [Test]
    public async Task ExecuteAsync_UsesPreviewAndExecution_WhenMediaFileExists()
    {
        // Arrange
        // Act
        await _subject.ExecuteAsync(_context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _contentFileService.Verify(
                e => e.PreviewRenameAsync(
                    _contentHierarchyItemId,
                    It.Is<FileInfo>(f => f.FullName == _mediaFile.FilePath),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentFileService.Verify(
                e => e.ExecuteRenameAsync(_preview, _userId, _correlationId, It.IsAny<CancellationToken>()),
                Times.Once);
            _progress.Verify(value => value.Report(0, "The rename job will load the media file."), Times.Once);
            _progress.Verify(value => value.Report(null, "The rename job will get the target path."), Times.Once);
            _progress.Verify(value => value.Report(null, "The rename job will rename the file."), Times.Once);
            _progress.Verify(value => value.Report(1, "The file rename completed."), Times.Once);
        }
    }

    [Test]
    public void ExecuteAsync_Throws_WhenMediaFileDoesNotExist()
    {
        // Arrange
        _mediaFiles
            .Setup(r => r.GetByIdAsync(_mediaFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MediaFile?)null);

        // Act
        // Assert
        Assert.That(
            async () => await _subject.ExecuteAsync(_context).ConfigureAwait(false),
            Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public void ExecuteAsync_Throws_WhenMediaFileHasNoContentItem()
    {
        // Arrange
        _contentItems
            .Setup(r => r.GetByIdAsync(_mediaFile.ContentItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentItem?)null);

        // Act
        // Assert
        Assert.That(
            async () => await _subject.ExecuteAsync(_context).ConfigureAwait(false),
            Throws.InstanceOf<InvalidOperationException>());
    }
}
