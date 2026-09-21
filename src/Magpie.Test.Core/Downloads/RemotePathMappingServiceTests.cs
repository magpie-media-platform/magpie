using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Caching;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Downloads;

[TestFixture]
public sealed class RemotePathMappingServiceTests
{
    private MemoryCacheFactory _cacheFactory = null!;
    private MemoryCacheStore _cacheStore = null!;
    private Mock<IDownloadClientRepository> _downloadClientRepository = null!;
    private IDownloadClientService _downloadClientService = null!;
    private Mock<IRemotePathMappingRepository> _remotePathMappingRepository = null!;
    private IRemotePathMappingService _service = null!;
    private string _testRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _cacheStore = new MemoryCacheStore(TimeProvider.System);
        _cacheFactory = new MemoryCacheFactory(_cacheStore);
        _testRoot = Path.Combine(Path.GetTempPath(), "magpie-remote-path-mapping-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
        _downloadClientRepository = new Mock<IDownloadClientRepository>();
        _downloadClientRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<DownloadClient>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DownloadClient client, Guid? _, CancellationToken _) => client);
        var downloadClientA = new Mock<IDownloadClientIntegration>();
        downloadClientA.SetupGet(integration => integration.ProviderType).Returns("provider-a");
        var downloadClientZ = new Mock<IDownloadClientIntegration>();
        downloadClientZ.SetupGet(integration => integration.ProviderType).Returns("provider-z");

        _downloadClientService = new DownloadClientService(
            _downloadClientRepository.Object,
            [downloadClientZ.Object, downloadClientA.Object],
            Mock.Of<ISettingsService>(),
            Mock.Of<IEventBus>(),
            TimeProvider.System);
        _remotePathMappingRepository = new Mock<IRemotePathMappingRepository>();
        _remotePathMappingRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<RemotePathMapping>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemotePathMapping mapping, Guid? _, CancellationToken _) => mapping);
        _remotePathMappingRepository
            .Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemotePathMapping
            {
                Host = "downloadbox.example.test",
                RemotePath = "/downloads",
                LocalPath = "/mnt/downloads"
            });
        _service = new RemotePathMappingService(
            _remotePathMappingRepository.Object,
            new FileSystem(
                Mock.Of<IBlockedFileTypeRepository>(),
                Mock.Of<IMimeMappingRepository>(),
                _cacheFactory,
                NullLogger<FileSystem>.Instance));
    }

    [TearDown]
    public void TearDown()
    {
        _cacheFactory.Dispose();
        _cacheStore.Dispose();
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public async Task CreateAsync_ReturnsDownloadClient_WhenProviderTypeIsAvailable()
    {
        // Arrange
        var client = new DownloadClient { ProviderType = "provider-a", Name = "Download client" };

        // Act
        var result = await _downloadClientService.CreateAsync(client, null).ConfigureAwait(false);

        // Assert
        Assert.That(result.ProviderType, Is.EqualTo("provider-a"));
    }

    [Test]
    public void ExtractHost_ReturnsHost_WhenValueIsUrl()
    {
        // Arrange
        var settings = new Dictionary<string, string> { ["host"] = "http://downloadbox.example.test:18080" };

        // Act
        // Assert
        Assert.That(_service.GetHost(settings), Is.EqualTo("downloadbox.example.test"));
    }

    [Test]
    public void ExtractHost_ReturnsHost_WhenValueIsBareHostWithPort()
    {
        // Arrange
        var settings = new Dictionary<string, string> { ["host"] = "downloadbox.example.test:18080" };

        // Act
        // Assert
        Assert.That(_service.GetHost(settings), Is.EqualTo("downloadbox.example.test"));
    }

    [Test]
    public void ExtractHost_ReturnsHost_WhenAlternateKeyContainsUrl()
    {
        // Arrange
        var settings = new Dictionary<string, string> { ["baseUrl"] = "https://seedbox.example:443/qbt" };

        // Act
        // Assert
        Assert.That(_service.GetHost(settings), Is.EqualTo("seedbox.example"));
    }

    [Test]
    public void ExtractHost_ReturnsNull_WhenNoHostSetting()
    {
        // Arrange
        var settings = new Dictionary<string, string> { ["username"] = "admin" };

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_service.GetHost(settings), Is.Null);
            Assert.That(_service.GetHost(null), Is.Null);
        }
    }

    [Test]
    public void ExtractHost_ReturnsNull_WhenUrlHasNoHost()
    {
        // Arrange
        var settings = new Dictionary<string, string> { ["host"] = "http://" };

        // Act
        // Assert
        Assert.That(_service.GetHost(settings), Is.Null);
    }

    [Test]
    public void ExtractHost_ReturnsIpv6Host_WhenValueUsesIpv6()
    {
        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _service.GetHost(new Dictionary<string, string> { ["host"] = "http://[::1]:9091/qbt" }),
                Is.EqualTo("::1"));
            Assert.That(
                _service.GetHost(new Dictionary<string, string> { ["host"] = "[::1]:9091" }),
                Is.EqualTo("::1"));
            Assert.That(
                _service.GetHost(new Dictionary<string, string> { ["host"] = "2001:db8::1" }),
                Is.EqualTo("2001:db8::1"));
        }
    }

    [Test]
    public void Apply_RewritesPrefix_WhenHostAndPrefixMatch()
    {
        // Arrange
        var mappings = new[]
        {
            new RemotePathMapping
            {
                Host = "downloadbox.example.test",
                RemotePath = @"E:\Transmission",
                LocalPath = @"\\downloadbox.example.test\share\Downloads"
            }
        };

        // Act
        var resolved = _service.Map(@"E:\Transmission\Gladiator (2000)", "downloadbox.example.test", mappings);

        // Assert
        Assert.That(resolved, Is.EqualTo(@"\\downloadbox.example.test\share\Downloads\Gladiator (2000)"));
    }

    [Test]
    public void Apply_RewritesPath_WhenHostAndPrefixCaseDiffer()
    {
        // Arrange
        var mappings = new[]
        {
            new RemotePathMapping
            {
                Host = "DownloadBox.Example.Test",
                RemotePath = @"E:\Transmission",
                LocalPath = @"\\host\share"
            }
        };

        // Act
        var resolved = _service.Map(@"e:\transmission\Movie", "downloadbox.example.test", mappings);

        // Assert
        Assert.That(resolved, Is.EqualTo(@"\\host\share\Movie"));
    }

    [Test]
    public void Apply_RewritesPath_WhenMappingHostRequiresNormalization()
    {
        // Arrange
        var mappings = new[]
        {
            new RemotePathMapping
            {
                Host = "http://downloadbox.example.test:18080/qbt",
                RemotePath = @"E:\Transmission",
                LocalPath = @"\\host\share"
            }
        };

        // Act
        var resolved = _service.Map(@"E:\Transmission\Movie", "downloadbox.example.test", mappings);

        // Assert
        Assert.That(resolved, Is.EqualTo(@"\\host\share\Movie"));
    }

    [Test]
    public void Apply_ReturnsUnchanged_WhenHostMismatch()
    {
        // Arrange
        var mappings = new[]
        {
            new RemotePathMapping
            {
                Host = "other.host",
                RemotePath = @"E:\Transmission",
                LocalPath = @"\\host\share"
            }
        };

        // Act
        var resolved = _service.Map(@"E:\Transmission\Movie", "downloadbox.example.test", mappings);

        // Assert
        Assert.That(resolved, Is.EqualTo(@"E:\Transmission\Movie"));
    }

    [Test]
    public void Apply_ReturnsUnchanged_WhenPrefixMatchesPartialSegment()
    {
        // Arrange
        var mappings = new[]
        {
            new RemotePathMapping { Host = "h", RemotePath = @"E:\Trans", LocalPath = @"\\x\y" }
        };

        // Act
        var resolved = _service.Map(@"E:\Transmission\Movie", "h", mappings);

        // Assert
        Assert.That(resolved, Is.EqualTo(@"E:\Transmission\Movie"));
    }

    [Test]
    public void Apply_UsesLongestPrefix_WhenMappingsOverlap()
    {
        // Arrange
        var mappings = new[]
        {
            new RemotePathMapping { Host = "h", RemotePath = @"E:\dl", LocalPath = @"\\x\broad" },
            new RemotePathMapping { Host = "h", RemotePath = @"E:\dl\movies", LocalPath = @"\\x\movies" }
        };

        // Act
        var resolved = _service.Map(@"E:\dl\movies\Film", "h", mappings);

        // Assert
        Assert.That(resolved, Is.EqualTo(@"\\x\movies\Film"));
    }

    [Test]
    public void Apply_RemovesTrailingSeparator_WhenRemotePathMatchesExactly()
    {
        // Arrange
        var mappings = new[]
        {
            new RemotePathMapping { Host = "h", RemotePath = @"E:\Transmission\", LocalPath = @"\\x\y\" }
        };

        // Act
        var resolved = _service.Map(@"E:\Transmission", "h", mappings);

        // Assert
        Assert.That(resolved, Is.EqualTo(@"\\x\y"));
    }

    [Test]
    public void Apply_NormalisesRemainderSeparators_WhenWindowsLocalPathIsRequested()
    {
        // Arrange
        var mappings = new[]
        {
            new RemotePathMapping { Host = "h", RemotePath = "/downloads", LocalPath = @"\\nas\dl" }
        };

        // Act
        var resolved = _service.Map("/downloads/movies/Film", "h", mappings);

        // Assert
        Assert.That(resolved, Is.EqualTo(@"\\nas\dl\movies\Film"));
    }

    [Test]
    public void Apply_NormalisesRemainderSeparators_WhenUnixLocalPathIsRequested()
    {
        // Arrange
        var mappings = new[]
        {
            new RemotePathMapping { Host = "h", RemotePath = @"E:\dl", LocalPath = "/mnt/dl" }
        };

        // Act
        var resolved = _service.Map(@"E:\dl\movies\Film", "h", mappings);

        // Assert
        Assert.That(resolved, Is.EqualTo("/mnt/dl/movies/Film"));
    }

    [Test]
    public void Apply_MatchesRegardlessOfHost_WhenClientHostUnknown()
    {
        // Arrange
        var mappings = new[]
        {
            new RemotePathMapping { Host = "downloadbox", RemotePath = @"E:\dl", LocalPath = @"\\x\y" }
        };

        // Act
        var resolved = _service.Map(@"E:\dl\Film", clientHost: null, mappings);

        // Assert
        Assert.That(resolved, Is.EqualTo(@"\\x\y\Film"));
    }

    [Test]
    public void Apply_ReturnsUnchanged_WhenNoMappings()
    {
        // Act
        var resolved = _service.Map(@"E:\dl\Film", "h", []);

        // Assert
        Assert.That(resolved, Is.EqualTo(@"E:\dl\Film"));
    }

    [Test]
    public async Task TestMappingLocalPath_ReturnsSuccess_WhenDirectoryExistsAndCanBeRead()
    {
        // Arrange
        var path = Path.Combine(_testRoot, "downloads");
        Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "sample.txt"), "sample").ConfigureAwait(false);

        // Act
        var result = await _service.TestLocalPathAsync($" {path} ", default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Is.EqualTo("Local path is reachable and readable."));
        }
    }

    [Test]
    public async Task TestMappingLocalPath_ReturnsFailure_WhenDirectoryDoesNotExist()
    {
        // Arrange
        var path = Path.Combine(_testRoot, "missing");

        // Act
        var result = await _service.TestLocalPathAsync(path, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("Local path does not exist or is not a directory."));
        }
    }

    [Test]
    public async Task TestMappingLocalPath_ReturnsFailure_WhenPathIsAFile()
    {
        // Arrange
        var path = Path.Combine(_testRoot, "download.txt");
        await File.WriteAllTextAsync(path, "sample").ConfigureAwait(false);

        // Act
        var result = await _service.TestLocalPathAsync(path, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("Local path does not exist or is not a directory."));
        }
    }

    [Test]
    public async Task TestMappingLocalPath_ReturnsFailure_WhenLocalPathIsBlank()
    {
        // Act
        var result = await _service.TestLocalPathAsync(" ", default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("Local path is required."));
        }
    }

    [TestCase("", "/downloads", "/mnt/downloads")]
    [TestCase("   ", "/downloads", "/mnt/downloads")]
    [TestCase("://", "/downloads", "/mnt/downloads")]
    [TestCase("http://", "/downloads", "/mnt/downloads")]
    [TestCase("downloadbox.example.test", "", "/mnt/downloads")]
    [TestCase("downloadbox.example.test", "   ", "/mnt/downloads")]
    [TestCase("downloadbox.example.test", "/downloads", "")]
    [TestCase("downloadbox.example.test", "/downloads", "   ")]
    public void CreateAsync_ThrowsArgumentException_WhenAValueIsEmptyOrHostIsMalformed(
        string host,
        string remotePath,
        string localPath)
    {
        // Arrange
        var create = new RemotePathMappingCreate { Host = host, RemotePath = remotePath, LocalPath = localPath };

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => _service.CreateAsync(create, null), Throws.ArgumentException);
            _remotePathMappingRepository.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<RemotePathMapping>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task CreateAsync_TrimsValues_WhenValuesHaveSurroundingWhitespace()
    {
        // Arrange
        var create = new RemotePathMappingCreate
        {
            Host = "  downloadbox.example.test  ",
            RemotePath = "  /downloads  ",
            LocalPath = "  /mnt/downloads  "
        };

        // Act
        var result = await _service.CreateAsync(create, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Host, Is.EqualTo("downloadbox.example.test"));
            Assert.That(result.RemotePath, Is.EqualTo("/downloads"));
            Assert.That(result.LocalPath, Is.EqualTo("/mnt/downloads"));
        }
    }

    [TestCase("", null, null)]
    [TestCase("   ", null, null)]
    [TestCase("://", null, null)]
    [TestCase(null, "   ", null)]
    [TestCase(null, null, "   ")]
    public void PatchAsync_ThrowsArgumentException_WhenAValueIsEmptyOrHostIsMalformed(
        string? host,
        string? remotePath,
        string? localPath)
    {
        // Arrange
        var patch = new RemotePathMappingPatch
        {
            Host = host is null ? null : new() { Value = host },
            RemotePath = remotePath is null ? null : new() { Value = remotePath },
            LocalPath = localPath is null ? null : new() { Value = localPath }
        };

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await _service.PatchAsync(Guid.NewGuid(), patch, null).ConfigureAwait(false),
                Throws.ArgumentException);
            _remotePathMappingRepository.Verify(
                repository => repository.PatchAsync(
                    It.IsAny<RemotePathMappingPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task PatchAsync_TrimsValues_WhenValuesHaveSurroundingWhitespace()
    {
        // Arrange
        var patch = new RemotePathMappingPatch
        {
            Host = new() { Value = "  seedbox.example  " },
            RemotePath = new() { Value = "  /remote  " },
            LocalPath = new() { Value = "  /local  " }
        };

        // Act
        await _service.PatchAsync(Guid.NewGuid(), patch, null).ConfigureAwait(false);

        // Assert
        _remotePathMappingRepository.Verify(
            repository => repository.PatchAsync(
                It.Is<RemotePathMappingPatch>(applied =>
                    applied.Host!.Value == "seedbox.example" &&
                    applied.RemotePath!.Value == "/remote" &&
                    applied.LocalPath!.Value == "/local"),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
