using Magpie.Extensions.Qbittorrent;
using Magpie.Models.Enums;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Qbittorrent;

[TestFixture]
public class QbittorrentStateExtensionsTests
{
    [TestCase("uploading", DownloadStatus.Downloaded)]
    [TestCase("seeding", DownloadStatus.Downloaded)]
    [TestCase("pausedUP", DownloadStatus.Downloaded)]
    [TestCase("stoppedUP", DownloadStatus.Downloaded)]
    [TestCase("queuedUP", DownloadStatus.Downloaded)]
    [TestCase("stalledUP", DownloadStatus.Downloaded)]
    [TestCase("checkingUP", DownloadStatus.Downloaded)]
    [TestCase("forcedUP", DownloadStatus.Downloaded)]
    [TestCase("downloading", DownloadStatus.Downloading)]
    [TestCase("forcedDL", DownloadStatus.Downloading)]
    [TestCase("metaDL", DownloadStatus.Downloading)]
    [TestCase("forcedMetaDL", DownloadStatus.Downloading)]
    [TestCase("pausedDL", DownloadStatus.Paused)]
    [TestCase("stoppedDL", DownloadStatus.Paused)]
    [TestCase("queuedDL", DownloadStatus.Queued)]
    [TestCase("checkingDL", DownloadStatus.Downloading)]
    [TestCase("checkingResumeData", DownloadStatus.Downloading)]
    [TestCase("allocating", DownloadStatus.Downloading)]
    [TestCase("moving", DownloadStatus.Downloading)]
    public void ToMappingResult_StatusWithoutReason_WhenStateIsKnown(string state, DownloadStatus expected)
    {
        // Arrange
        // Act
        var result = state.ToMappingResult();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(expected));
            Assert.That(result.StatusReason, Is.Null);
        }
    }

    [Test]
    public void ToMappingResult_FailedWithReason_WhenStateIsError()
    {
        // Arrange
        // Act
        var result = "error".ToMappingResult();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(DownloadStatus.Failed));
            Assert.That(result.StatusReason, Does.Contain("error"));
            Assert.That(result.StatusReason, Does.Contain("qBittorrent state"));
        }
    }

    [Test]
    public void ToMappingResult_FailedWithReason_WhenStateIsMissingFiles()
    {
        // Arrange
        // Act
        var result = "missingFiles".ToMappingResult();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(DownloadStatus.Failed));
            Assert.That(result.StatusReason, Does.Contain("missingFiles"));
        }
    }

    [Test]
    public void ToMappingResult_StalledWithReason_WhenStateIsStalledDownload()
    {
        // Arrange
        // Act
        var result = "stalledDL".ToMappingResult();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(DownloadStatus.Stalled));
            Assert.That(result.StatusReason, Does.Contain("stalledDL"));
        }
    }

    [Test]
    public void ToMappingResult_StalledWithReason_WhenStateIsDocumentedUnknown()
    {
        // Arrange
        // Act
        var result = "unknown".ToMappingResult();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(DownloadStatus.Stalled));
            Assert.That(result.StatusReason, Does.Contain("unknown"));
        }
    }

    [Test]
    public void ToMappingResult_GrabbedWithLiteralReason_WhenStateIsUnrecognized()
    {
        // Arrange
        // Act
        var result = "invented_future_state".ToMappingResult();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(DownloadStatus.Grabbed));
            Assert.That(result.StatusReason, Does.Contain("invented_future_state"));
        }
    }

    [Test]
    public void ToMappingResult_GrabbedWithDiagnosticReason_WhenStateIsEmpty()
    {
        // Arrange
        // Act
        var result = string.Empty.ToMappingResult();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(DownloadStatus.Grabbed));
            Assert.That(result.StatusReason, Does.Contain("no state"));
        }
    }

    [Test]
    public void ToMappingResult_GrabbedWithDiagnosticReason_WhenStateIsNull()
    {
        // Arrange
        // Act
        var result = ((string?)null).ToMappingResult();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(DownloadStatus.Grabbed));
            Assert.That(result.StatusReason, Does.Contain("no state"));
        }
    }
}
