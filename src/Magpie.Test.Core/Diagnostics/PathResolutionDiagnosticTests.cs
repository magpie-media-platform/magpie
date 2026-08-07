using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Diagnostics;
using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Diagnostics;

[TestFixture]
public sealed class PathResolutionDiagnosticTests
{
    private static readonly string ReadablePath = Path.GetTempPath();

    private static readonly string MissingPath =
        Path.Combine(Path.GetTempPath(), "magpie-diagnostic-missing-" + Guid.NewGuid().ToString("N"));

    private Mock<IRootFolderRepository> _rootFolders = null!;
    private Mock<IRemotePathMappingRepository> _remotePathMappings = null!;
    private Mock<IRemotePathMappingService> _remotePathMappingService = null!;
    private IDiagnosticCheck _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _rootFolders = new Mock<IRootFolderRepository>(MockBehavior.Strict);
        _remotePathMappings = new Mock<IRemotePathMappingRepository>(MockBehavior.Strict);
        _remotePathMappingService = new Mock<IRemotePathMappingService>(MockBehavior.Strict);
        _subject = new PathResolutionDiagnostic(
            _rootFolders.Object,
            _remotePathMappings.Object,
            _remotePathMappingService.Object);
    }

    [Test]
    public async Task RunAsync_ProducesWarning_WhenRootFolderPathIsUnreadable()
    {
        // Arrange
        var folder = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "Movies",
            Path = MissingPath,
            ContentType = "movie",
            CreatedAt = DateTime.UtcNow,
        };
        _rootFolders.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([folder]);
        _remotePathMappings.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _remotePathMappingService
            .Setup(service => service.TestLocalPathAsync(MissingPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigurationTestResult { Success = false, Message = "missing" });

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(findings, Has.Count.EqualTo(1));
            Assert.That(findings[0].Severity, Is.EqualTo(DiagnosticFindingSeverity.Warning));
            Assert.That(findings[0].DiagnosticCheckId, Is.EqualTo(PathResolutionDiagnostic.Id));
            Assert.That(findings[0].AffectedEntity, Is.EqualTo($"RootFolder:{folder.Id}"));
        }
    }

    [Test]
    public async Task RunAsync_ProducesWarning_WhenRemotePathMappingDoesNotResolve()
    {
        // Arrange
        var mapping = new RemotePathMapping
        {
            Id = Guid.NewGuid(),
            Host = "downloader.local",
            RemotePath = "/remote/downloads",
            LocalPath = MissingPath,
            CreatedAt = DateTime.UtcNow,
        };
        _rootFolders.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _remotePathMappings.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([mapping]);
        _remotePathMappingService
            .Setup(service => service.TestLocalPathAsync(MissingPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigurationTestResult { Success = false, Message = "missing" });

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(findings, Has.Count.EqualTo(1));
            Assert.That(findings[0].Severity, Is.EqualTo(DiagnosticFindingSeverity.Warning));
            Assert.That(findings[0].AffectedEntity, Is.EqualTo($"RemotePathMapping:{mapping.Id}"));
        }
    }

    [Test]
    public async Task RunAsync_ProducesNoFinding_WhenEveryPathResolves()
    {
        // Arrange
        _rootFolders.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RootFolder
                {
                    Id = Guid.NewGuid(),
                    Name = "Movies",
                    Path = ReadablePath,
                    ContentType = "movie",
                    CreatedAt = DateTime.UtcNow,
                }
            ]);
        _remotePathMappings.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RemotePathMapping
                {
                    Id = Guid.NewGuid(),
                    Host = "downloader.local",
                    RemotePath = "/remote/downloads",
                    LocalPath = ReadablePath,
                    CreatedAt = DateTime.UtcNow,
                }
            ]);
        _remotePathMappingService
            .Setup(service => service.TestLocalPathAsync(ReadablePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigurationTestResult { Success = true, Message = "ok" });

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(findings, Is.Empty);
    }
}
