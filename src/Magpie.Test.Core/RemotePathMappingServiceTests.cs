using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class RemotePathMappingServiceTests
{
    private Mock<IFileSystem> _fileSystem = null!;
    private Mock<ISettingsService> _settingsService = null!;
    private IRemotePathMappingService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _fileSystem = new Mock<IFileSystem>();
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/data/downloads")).Returns(true);
        _settingsService = new Mock<ISettingsService>();
        _subject = new RemotePathMappingService(
            Mock.Of<IRemotePathMappingRepository>(),
            _fileSystem.Object,
            _settingsService.Object);
    }

    [Test]
    public async Task TestLocalPathAsync_NamesTheInstance_WhenAccessIsDenied()
    {
        // Arrange
        _settingsService
            .Setup(service => service.GetInstanceNameAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("Home Lab");
        _fileSystem
            .Setup(fileSystem => fileSystem.EnumerateFileSystemEntries("/data/downloads"))
            .Throws<UnauthorizedAccessException>();

        // Act
        var result = await _subject.TestLocalPathAsync("/data/downloads").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("Local path exists but Home Lab cannot read it."));
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.Auth));
        }
    }

    [Test]
    public async Task TestLocalPathAsync_ReturnsAuthFailure_WhenReadingTheLocalPathThrowsIOException()
    {
        // Arrange
        _settingsService
            .Setup(service => service.GetInstanceNameAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("Magpie");
        _fileSystem
            .Setup(fileSystem => fileSystem.EnumerateFileSystemEntries("/data/downloads"))
            .Throws<IOException>();

        // Act
        var result = await _subject.TestLocalPathAsync("/data/downloads").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("Local path exists but Magpie cannot read it."));
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.Auth));
        }
    }
}
