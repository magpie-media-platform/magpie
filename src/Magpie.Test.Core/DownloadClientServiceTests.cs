using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class DownloadClientServiceTests
{
    private Mock<IDownloadClientRepository> _downloadClientRepository = null!;
    private IDownloadClientService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _downloadClientRepository = new Mock<IDownloadClientRepository>();
        _downloadClientRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<DownloadClient>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DownloadClient client, Guid? _, CancellationToken _) => client);
        _downloadClientRepository
            .Setup(repository => repository.PatchAsync(
                It.IsAny<DownloadClientPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _subject = new DownloadClientService(
            _downloadClientRepository.Object,
            [],
            Mock.Of<ISettingsService>(),
            Mock.Of<IEventBus>(),
            TimeProvider.System);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void CreateAsync_Throws_WhenNameIsNullOrBlank(string? name)
    {
        // Arrange
        var client = new DownloadClient
        {
            ProviderType = "qbittorrent",
            Name = name!
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateAsync(client, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("Name is required."));
            _downloadClientRepository.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<DownloadClient>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void CreateAsync_Throws_WhenProviderTypeIsNullOrBlank(string? providerType)
    {
        // Arrange
        var client = new DownloadClient
        {
            ProviderType = providerType!,
            Name = "Primary"
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateAsync(client, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("Provider type is required."));
            _downloadClientRepository.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<DownloadClient>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task CreateAsync_CreatesClient_WhenNameIsValid()
    {
        // Arrange
        var client = new DownloadClient
        {
            ProviderType = "qbittorrent",
            Name = "Primary"
        };

        // Act
        var created = await _subject.CreateAsync(client, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Name, Is.EqualTo("Primary"));
            _downloadClientRepository.Verify(
                repository => repository.CreateAsync(client, null, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void PatchAsync_Throws_WhenNameIsNullOrBlank(string? name)
    {
        // Arrange
        var patch = new DownloadClientPatch { Name = new() { Value = name! } };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() =>
            _subject.PatchAsync(Guid.NewGuid(), patch, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("Name is required."));
            _downloadClientRepository.Verify(
                repository => repository.PatchAsync(
                    It.IsAny<DownloadClientPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesClient_WhenNameIsValid()
    {
        // Arrange
        var patch = new DownloadClientPatch { Name = new() { Value = "Renamed" } };

        // Act
        var patched = await _subject.PatchAsync(Guid.NewGuid(), patch, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Is.True);
            _downloadClientRepository.Verify(
                repository => repository.PatchAsync(
                    It.Is<DownloadClientPatch>(applied => applied.Name!.Value == "Renamed"),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
