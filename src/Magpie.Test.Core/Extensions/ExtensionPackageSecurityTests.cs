using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Caching;
using Magpie.Core.Config;
using Magpie.Core.Extensions;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;
using Moq.Protected;

using NUnit.Framework;

namespace Magpie.Test.Core.Extensions;

[TestFixture]
public sealed class ExtensionPackageSecurityTests
{
    private Mock<HttpMessageHandler> _handler = null!;
    private HttpClient _client = null!;
    private MemoryCacheStore _cacheStore = null!;
    private MemoryCacheFactory _cacheFactory = null!;
    private Mock<IExtensionPackageSourceRepository> _sources = null!;
    private Mock<IExtensionRepository> _extensions = null!;
    private Mock<IFileSystem> _files = null!;
    private ExtensionPackageSource _source = null!;
    private ExtensionService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        _handler.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        _client = new HttpClient(_handler.Object);
        _cacheStore = new MemoryCacheStore(TimeProvider.System);
        _cacheFactory = new MemoryCacheFactory(_cacheStore);
        _sources = new Mock<IExtensionPackageSourceRepository> { CallBase = true };
        _files = new Mock<IFileSystem>(MockBehavior.Strict);
        _source = new ExtensionPackageSource
        {
            Name = "Source",
            SourceUrl = "https://source.example/v3/index.json",
            Enabled = true,
            CredentialKind = ExtensionPackageSourceCredentialKind.Basic,
            Username = "user",
            EncryptedSecret = "secret",
        };
        _sources.Setup(value => value.QueryAsync(It.IsAny<ExtensionPackageSourceQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ExtensionPackageSource> { Items = [_source] });
        _extensions = new Mock<IExtensionRepository> { CallBase = true };
        _extensions.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new() { Id = Guid.NewGuid(), Name = "Package", PackageId = "Package", Version = "1.0.0", Managed = true,
                FilePath = "package.dll", AssemblyName = "Package" },
        ]);
        _subject = new ExtensionService(
            Mock.Of<IHttpClientFactory>(factory => factory.CreateClient(It.IsAny<string>()) == _client),
            _extensions.Object,
            _sources.Object,
            Mock.Of<IExtensionActivationGenerationRepository>(),
            new("node", false, false, ".", ".", ".", ".", DateTime.UtcNow),
            new MagpieHostOptions(),
            Mock.Of<IExtensionLifecycleManager>(),
            Mock.Of<IEventBus>(),
            _cacheFactory,
            _files.Object,
            TimeProvider.System,
            NullLogger<ExtensionService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _cacheFactory.Dispose();
        _cacheStore.Dispose();
    }

    [TestCase("http://source.example/index.json")]
    [TestCase("relative/index.json")]
    [TestCase("https://user:secret@source.example/index.json")]
    public void CreatePackageSourceAsync_RejectsUnsafeUrl_WhenSourceIsConfigured(string url)
    {
        // Arrange
        var create = new ExtensionPackageSourceCreate { Name = "Source", SourceUrl = url };

        // Act
        var error = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreatePackageSourceAsync(create, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("Source URL must be an absolute HTTPS URL."));
            _sources.Verify(value => value.CreateAsync(It.IsAny<ExtensionPackageSource>(), null,
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase("http://source.example/index.json")]
    [TestCase("relative/index.json")]
    public void PatchPackageSourceAsync_RejectsUnsafeUrl_WhenSourceUrlChanges(string url)
    {
        // Arrange
        var patch = new ExtensionPackageSourcePatch { SourceUrl = new() { Value = url } };

        // Act
        var error = Assert.ThrowsAsync<ArgumentException>(() => _subject.PatchPackageSourceAsync(Guid.NewGuid(),
            patch, null));

        // Assert
        Assert.That(error!.Message, Is.EqualTo("Source URL must be an absolute HTTPS URL."));
    }

    [TestCase("https://source.example/query", true)]
    [TestCase("https://other.example/query", false)]
    [TestCase("https://source.example:444/query", false)]
    public async Task SearchAsync_RestrictsCredentials_WhenIndexSuppliesAnEndpoint(string endpoint,
        bool sendsCredentials)
    {
        // Arrange
        _handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                     "https://source.example/v3/index.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"resources\":[{\"@type\":\"SearchQueryService\",\"@id\":\"" + endpoint + "\"}]}",
                    Encoding.UTF8, "application/json"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri.StartsWith(endpoint)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":[]}", Encoding.UTF8, "application/json"),
            });

        // Act
        await _subject.SearchAsync("package").ConfigureAwait(false);

        // Assert
        _handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri.StartsWith(endpoint) &&
                (request.Headers.Authorization != null) == sendsCredentials), ItExpr.IsAny<CancellationToken>());
    }

    [TestCase("1.1.0-beta", "1.0.0", false)]
    [TestCase("1.1.0", "1.1.0", true)]
    public async Task ListAsync_SelectsStableVersion_WhenVersionIndexContainsPrereleases(string lastVersion,
        string expected, bool hasUpdate)
    {
        // Arrange
        _source.SourceUrl = "https://source.example/v3-flatcontainer";
        _handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"versions\":[\"1.0.0\",\"" + lastVersion + "\"]}", Encoding.UTF8,
                    "application/json"),
            });

        // Act
        var result = await _subject.ListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result[0].UpdateInfo!.LatestVersion, Is.EqualTo(expected));
            Assert.That(result[0].UpdateInfo!.HasUpdate, Is.EqualTo(hasUpdate));
        }
    }

    [Test]
    public async Task ListAsync_ReportsTheNewestCompatibleVersion_WhenTheNewestVersionNeedsANewerHostAbi()
    {
        // Arrange
        _source.SourceUrl = "https://source.example/v3-flatcontainer";
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/index.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"versions\":[\"1.0.0\",\"1.1.0\",\"99.0.0\"]}",
                    Encoding.UTF8,
                    "application/json"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/99.0.0/package.nuspec"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                        <package><metadata><dependencies><group targetFramework=".NETStandard2.1">
                        <dependency id="Magpie.Sdk" version="[99.0.0, 100.0.0)" />
                        </group></dependencies></metadata></package>
                        """,
                    Encoding.UTF8,
                    "application/xml"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/1.1.0/package.nuspec"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"""
                        <package><metadata><dependencies><group targetFramework=".NETStandard2.1">
                        <dependency id="Magpie.Sdk" version="{ExtensionLoader.CurrentAbiVersion.Format()}" />
                        </group></dependencies></metadata></package>
                        """,
                    Encoding.UTF8,
                    "application/xml"),
            });

        // Act
        var result = await _subject.ListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result[0].UpdateInfo!.LatestVersion, Is.EqualTo("1.1.0"));
            Assert.That(result[0].UpdateInfo!.HasUpdate, Is.True);
        }
    }

    [TestCase("{\"versions\":[\"1.0.0\"]}", "1.0.0", true)]
    [TestCase("{\"versions\":[\"1.1.0\"]}", "1.1.0", true)]
    [TestCase("{\"versions\":[\"0.9.0\"]}", "0.9.0", false)]
    public async Task ListAsync_ReportsAnUpdateOnlyForAVersionThatIsNotOlder_WhenTheExtensionIsUnmanaged(
        string versionIndex,
        string latestVersion,
        bool hasUpdate)
    {
        // Arrange
        _source.SourceUrl = "https://source.example/v3-flatcontainer";
        _extensions.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Package",
                PackageId = "Package",
                Version = "1.0.0",
                Managed = false,
                FilePath = "package.dll",
                AssemblyName = "Package"
            },
        ]);
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.RequestUri!.AbsoluteUri == "https://source.example/v3-flatcontainer/package/index.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(versionIndex, Encoding.UTF8, "application/json"),
            });
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri.EndsWith("/package.nuspec")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        // Act
        var result = await _subject.ListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result[0].UpdateInfo!.LatestVersion, Is.EqualTo(latestVersion));
            Assert.That(result[0].UpdateInfo!.HasUpdate, Is.EqualTo(hasUpdate));
        }
    }

    [Test]
    public async Task ListAsync_ChecksTheSourcesAgain_WhenAPackageSourceIsAdded()
    {
        // Arrange
        _source.SourceUrl = "https://source.example/v3-flatcontainer";
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.RequestUri!.AbsoluteUri == "https://source.example/v3-flatcontainer/package/index.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"versions\":[\"1.0.0\"]}", Encoding.UTF8, "application/json"),
            });
        await _subject.ListAsync().ConfigureAwait(false);

        // Act
        await _subject.CreatePackageSourceAsync(
            new ExtensionPackageSourceCreate { Name = "Feed", SourceUrl = "https://feed.example/v3/index.json" },
            null).ConfigureAwait(false);
        await _subject.ListAsync().ConfigureAwait(false);

        // Assert
        _handler.Protected()
            .Verify(
                "SendAsync",
                Times.Exactly(2),
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.RequestUri!.AbsoluteUri == "https://source.example/v3-flatcontainer/package/index.json"),
                ItExpr.IsAny<CancellationToken>());
    }

    [TestCase(
        "{\"versions\":[\"0.9.0\"]}",
        "https://source.example/v3-flatcontainer/package/0.9.0/package.0.9.0.nupkg",
        0)]
    [TestCase(
        "{\"versions\":[\"0.9.0\",\"1.0.0\"]}",
        "https://source.example/v3-flatcontainer/package/1.0.0/package.1.0.0.nupkg",
        1)]
    [TestCase(
        "{\"versions\":[\"1.1.0\"]}",
        "https://source.example/v3-flatcontainer/package/1.1.0/package.1.1.0.nupkg",
        1)]
    public void UpgradeToLatestAsync_DownloadsOnlyAVersionThatIsNotOlder_WhenTheExtensionIsUnmanaged(
        string versionIndex,
        string packageUrl,
        int downloads)
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        _source.SourceUrl = "https://source.example/v3-flatcontainer";
        _extensions.Setup(value => value.GetByIdAsync(extensionId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new Extension
            {
                Id = extensionId,
                Name = "Package",
                PackageId = "Package",
                Version = "1.0.0",
                Managed = false,
                FilePath = "/extensions/package/Package.dll",
                AssemblyName = "Package"
            });
        _files.Setup(value => value.GetFullPath(".")).Returns("/extensions");
        _files.Setup(value => value.GetFullPath("/extensions/package/Package.dll"))
            .Returns("/extensions/package/Package.dll");
        _files.Setup(value => value.IsPathWithin("/extensions", "/extensions/package/Package.dll")).Returns(true);
        _files.Setup(value => value.PathsEqual("/extensions", It.IsAny<string>())).Returns(false);
        _files.Setup(value => value.GetRelativePath("/extensions", It.IsAny<string>())).Returns("package");
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.RequestUri!.AbsoluteUri == "https://source.example/v3-flatcontainer/package/index.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(versionIndex, Encoding.UTF8, "application/json"),
            });
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => !request.RequestUri!.AbsoluteUri.EndsWith("/index.json")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        // Act
        var error = Assert.ThrowsAsync<InvalidOperationException>(
            () => _subject.UpgradeToLatestAsync(extensionId, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("NuGet package Package could not be downloaded."));
            _handler.Protected()
                .Verify(
                    "SendAsync",
                    Times.Exactly(downloads),
                    ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri == packageUrl),
                    ItExpr.IsAny<CancellationToken>());
        }
    }

    [Test]
    public void InstallFromSourceAsync_DownloadsTheNewestCompatibleVersion_WhenTheNewestVersionNeedsANewerHostAbi()
    {
        // Arrange
        _source.SourceUrl = "https://source.example/v3-flatcontainer";
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/index.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"versions\":[\"1.0.0\",\"99.0.0\"]}", Encoding.UTF8, "application/json"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/99.0.0/package.nuspec"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                        <package><metadata><dependencies>
                        <dependency id="Magpie.Sdk" version="[99.0.0, 100.0.0)" />
                        </dependencies></metadata></package>
                        """,
                    Encoding.UTF8,
                    "application/xml"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/1.0.0/package.nuspec"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"""
                        <package><metadata><dependencies>
                        <dependency id="Magpie.Sdk" version="{ExtensionLoader.CurrentAbiVersion.Format()}" />
                        </dependencies></metadata></package>
                        """,
                    Encoding.UTF8,
                    "application/xml"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri.EndsWith(".nupkg")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        // Act
        var error = Assert.ThrowsAsync<InvalidOperationException>(
            () => _subject.InstallFromSourceAsync("Package", null, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("NuGet package Package could not be downloaded."));
            _handler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/1.0.0/package.1.0.0.nupkg"),
                ItExpr.IsAny<CancellationToken>());
            _handler.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/99.0.0/package.99.0.0.nupkg"),
                ItExpr.IsAny<CancellationToken>());
        }
    }

    [Test]
    public void InstallFromSourceAsync_ThrowsTheNeededHostAbi_WhenNoStableVersionIsCompatible()
    {
        // Arrange
        _source.SourceUrl = "https://source.example/v3-flatcontainer";
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/index.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"versions\":[\"99.0.0\"]}", Encoding.UTF8, "application/json"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/99.0.0/package.nuspec"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                        <package><metadata><dependencies>
                        <dependency id="Magpie.Sdk" version="[99.0.0, 100.0.0)" />
                        </dependencies></metadata></package>
                        """,
                    Encoding.UTF8,
                    "application/xml"),
            });

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() => _subject.InstallFromSourceAsync("Package", null, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                error!.Message,
                Does.StartWith("No stable version of NuGet package Package is compatible with host ABI "));
            Assert.That(error.Message, Does.EndWith("; version 99.0.0 needs host ABI 99.0.0 or a later 99.x version."));
            _handler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/99.0.0/package.nuspec"),
                ItExpr.IsAny<CancellationToken>());
        }
    }

    [Test]
    public async Task SearchAsync_ReportsTheNewestCompatibleVersion_WhenTheNewestVersionNeedsANewerHostAbi()
    {
        // Arrange
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3/index.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                        {"resources":[{"@type":"SearchQueryService","@id":"https://source.example/query"},
                        {"@type":"PackageBaseAddress/3.0.0","@id":"https://source.example/v3-flatcontainer/"}]}
                        """,
                    Encoding.UTF8,
                    "application/json"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.RequestUri!.AbsoluteUri.StartsWith("https://source.example/query")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                        {"data":[{"id":"Package","version":"99.0.0","versions":[{"version":"1.0.0"},{"version":"99.0.0"}]}]}
                        """,
                    Encoding.UTF8,
                    "application/json"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/99.0.0/package.nuspec"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                        <package><metadata><dependencies>
                        <dependency id="Magpie.Sdk" version="[99.0.0, 100.0.0)" />
                        </dependencies></metadata></package>
                        """,
                    Encoding.UTF8,
                    "application/xml"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/1.0.0/package.nuspec"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"""
                        <package><metadata><dependencies>
                        <dependency id="Magpie.Sdk" version="{ExtensionLoader.CurrentAbiVersion.Format()}" />
                        </dependencies></metadata></package>
                        """,
                    Encoding.UTF8,
                    "application/xml"),
            });

        // Act
        var results = await _subject.SearchAsync("package").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Version, Is.EqualTo("1.0.0"));
        }
    }

    [TestCase("..")]
    [TestCase("...")]
    public void UploadAsync_RejectsUnsafeSegment_WhenDirectoryNameEscapesRoot(string name)
    {
        // Arrange
        using var stream = new MemoryStream();

        // Act
        var error = Assert.ThrowsAsync<ArgumentException>(() => _subject.UploadAsync(stream, name, null));

        // Assert
        Assert.That(error!.Message, Does.StartWith("The extension directory name is invalid."));
    }

    [Test]
    public void InstallFromSourceAsync_ThrowsPrereleaseError_WhenTheFeedPublishesPrereleaseVersionsOnly()
    {
        // Arrange
        _handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3/index.json"), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"version":"3.0.0","resources":[
                      {"@type":"PackageBaseAddress/3.0.0","@id":"https://source.example/v3-flatcontainer/"},
                      {"@type":"RegistrationsBaseUrl","@id":"https://source.example/registration/"}
                    ]}
                    """, Encoding.UTF8, "application/json"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/index.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"versions\":[\"1.0.0-alpha\",\"1.0.0-beta.2\"]}", Encoding.UTF8, "application/json"),
            });

        // Act
        var error = Assert.ThrowsAsync<InvalidOperationException>(
            () => _subject.InstallFromSourceAsync("Package", null, null));

        // Assert
        Assert.That(
            error!.Message,
            Is.EqualTo("NuGet package Package publishes prerelease versions only."));
    }

    [TestCase(
        "RegistrationsBaseUrl/Versioned",
        "registration5-gz-semver2",
        """
        {"items":[{"items":[{"catalogEntry":{"version":"1.0.0",
        "@id":"https://api.nuget.org/v3/catalog0/package.json"}}]}]}
        """)]
    [TestCase(
        "RegistrationsBaseUrl/3.0.0-beta",
        "registration5-semver1",
        """
        {"items":[{"items":[{"catalogEntry":{"version":"1.0.0",
        "@id":"https://api.nuget.org/v3/catalog0/package.json"}}]}]}
        """)]
    [TestCase(
        "RegistrationsBaseUrl/Versioned",
        "registration5-gz-semver2",
        """{"items":[{"items":[{"catalogEntry":"https://api.nuget.org/v3/catalog0/package.json"}]}]}""")]
    public void InstallFromSourceAsync_Throws_WhenThePackageDoesNotMatchTheRegistrationHash(
        string lastResourceType,
        string lastResourcePath,
        string registrationIndex)
    {
        // Arrange
        _source.SourceUrl = "https://api.nuget.org/v3/index.json";
        _source.CredentialKind = ExtensionPackageSourceCredentialKind.None;
        _handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://api.nuget.org/v3/index.json"), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""
                    {"version":"3.0.0","resources":[
                      {"@type":"SearchQueryService","@id":"https://azuresearch-usnc.nuget.org/query"},
                      {"@type":"RegistrationsBaseUrl","@id":"https://api.nuget.org/v3/registration5-semver1/"},
                      {"@type":"PackageBaseAddress/3.0.0","@id":"https://api.nuget.org/v3-flatcontainer/"},
                      {"@type":"RegistrationsBaseUrl/3.0.0-rc","@id":"https://api.nuget.org/v3/registration5-semver1/"},
                      {"@type":"RegistrationsBaseUrl/3.4.0","@id":"https://api.nuget.org/v3/registration5-gz-semver1/"},
                      {"@type":"RegistrationsBaseUrl/3.6.0","@id":"https://api.nuget.org/v3/registration5-gz-semver2/"},
                      {"@type":"{{lastResourceType}}","@id":"https://api.nuget.org/v3/{{lastResourcePath}}/"}
                    ]}
                    """, Encoding.UTF8, "application/json"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://api.nuget.org/v3-flatcontainer/package/1.0.0/package.1.0.0.nupkg"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("changed")),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://api.nuget.org/v3/registration5-gz-semver2/package/index.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(registrationIndex, Encoding.UTF8, "application/json"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://api.nuget.org/v3/catalog0/package.json"), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"id":"Package","version":"1.0.0","packageHashAlgorithm":"SHA512",
                    "packageHash":"baSbv6lplk3RxzbxKOYcpdxTiKsd8MTis+A+sH/42atj0u0v1arIbDTS0oWZfshbD05M2SlToEnxIPGgmtbw2g=="}
                    """, Encoding.UTF8, "application/json"),
            });

        // Act
        var error = Assert.ThrowsAsync<CryptographicException>(
            () => _subject.InstallFromSourceAsync("Package", "1.0.0", null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("The package hash does not match the source metadata."));
            _handler.Protected().Verify("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://api.nuget.org/v3/registration5-gz-semver2/package/index.json"),
                ItExpr.IsAny<CancellationToken>());
        }
    }

    [Test]
    public void InstallFromSourceAsync_ChecksTheExtensionDirectory_WhenTheRegistrationPublishesNoPackageHash()
    {
        // Arrange
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3/index.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                        {"version":"3.0.0","resources":[
                          {"@type":"PackageBaseAddress/3.0.0","@id":"https://source.example/v3-flatcontainer/"},
                          {"@type":"RegistrationsBaseUrl/3.6.0","@id":"https://source.example/v3/registration/"}
                        ]}
                        """,
                    Encoding.UTF8,
                    "application/json"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/1.0.0/package.1.0.0.nupkg"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("package")),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3/registration/package/index.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                        {"items":[{"items":[{"catalogEntry":{
                        "@id":"https://source.example/v3/catalog/package/1.0.0.json","authors":"Author",
                        "dependencyGroups":[],"description":"Package","iconUrl":"","id":"Package","licenseUrl":"",
                        "listed":true,"minClientVersion":"","projectUrl":"","published":"2026-10-06T00:00:00+00:00",
                        "requireLicenseAcceptance":false,"summary":"","tags":[],"title":"Package","version":"1.0.0"}}]}]}
                        """,
                    Encoding.UTF8,
                    "application/json"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3/catalog/package/1.0.0.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                        {"@id":"https://source.example/v3/catalog/package/1.0.0.json","authors":"Author",
                        "dependencyGroups":[],"description":"Package","iconUrl":"","id":"Package","licenseUrl":"",
                        "listed":true,"minClientVersion":"","projectUrl":"","published":"2026-10-06T00:00:00+00:00",
                        "requireLicenseAcceptance":false,"summary":"","tags":[],"title":"Package","version":"1.0.0"}
                        """,
                    Encoding.UTF8,
                    "application/json"),
            });
        _files.Setup(value => value.CreateDirectory("."));
        _files.Setup(value => value.GetFullPath(It.IsAny<string>())).Returns("/outside/Package");
        _files.Setup(value => value.IsPathWithin(".", "/outside/Package")).Returns(false);

        // Act
        var error = Assert.ThrowsAsync<ArgumentException>(
            () => _subject.InstallFromSourceAsync("Package", "1.0.0", null));

        // Assert
        Assert.That(error!.Message, Does.StartWith("The extension directory is outside the extensions root."));
    }

    [TestCase("""{"id":"Package","version":"1.0.0","packageHashAlgorithm":"SHA512"}""")]
    [TestCase("""
        {"id":"Package","version":"1.0.0",
        "packageHash":"baSbv6lplk3RxzbxKOYcpdxTiKsd8MTis+A+sH/42atj0u0v1arIbDTS0oWZfshbD05M2SlToEnxIPGgmtbw2g=="}
        """)]
    public void InstallFromSourceAsync_Throws_WhenTheRegistrationPublishesAnIncompletePackageHash(string metadata)
    {
        // Arrange
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3/index.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                        {"version":"3.0.0","resources":[
                          {"@type":"PackageBaseAddress/3.0.0","@id":"https://source.example/v3-flatcontainer/"},
                          {"@type":"RegistrationsBaseUrl/3.6.0","@id":"https://source.example/v3/registration/"}
                        ]}
                        """,
                    Encoding.UTF8,
                    "application/json"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/1.0.0/package.1.0.0.nupkg"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("package")),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3/registration/package/index.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                        {"items":[{"items":[{"catalogEntry":{"version":"1.0.0",
                        "@id":"https://source.example/v3/catalog/package/1.0.0.json"}}]}]}
                        """,
                    Encoding.UTF8,
                    "application/json"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3/catalog/package/1.0.0.json"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(metadata, Encoding.UTF8, "application/json"),
            });

        // Act
        var error = Assert.ThrowsAsync<InvalidOperationException>(
            () => _subject.InstallFromSourceAsync("Package", "1.0.0", null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("NuGet package Package could not be downloaded."));
            _handler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3/catalog/package/1.0.0.json"),
                ItExpr.IsAny<CancellationToken>());
        }
    }

    [Test]
    public void InstallFromSourceAsync_Throws_WhenTheSourcePublishesNoIntegrityMetadata()
    {
        // Arrange
        _handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3/index.json"), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"version":"3.0.0","resources":[
                      {"@type":"PackageBaseAddress/3.0.0","@id":"https://source.example/v3-flatcontainer/"}
                    ]}
                    """, Encoding.UTF8, "application/json"),
            });
        _handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsoluteUri ==
                    "https://source.example/v3-flatcontainer/package/1.0.0/package.1.0.0.nupkg"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("package")),
            });

        // Act
        var error = Assert.ThrowsAsync<InvalidOperationException>(
            () => _subject.InstallFromSourceAsync("Package", "1.0.0", null));

        // Assert
        Assert.That(error!.Message, Is.EqualTo("NuGet package Package could not be downloaded."));
    }

    [Test]
    public async Task SearchAsync_CallsSourceWithoutCredentials_WhenSourceUrlIsNotHttps()
    {
        // Arrange
        _source.SourceUrl = "http://source.example/query";
        _source.CredentialKind = ExtensionPackageSourceCredentialKind.None;
        _handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[]}", Encoding.UTF8, "application/json"),
            });

        // Act
        var results = await _subject.SearchAsync("package").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results, Is.Empty);
            _handler.Protected().Verify("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(request => request.Headers.Authorization == null),
                ItExpr.IsAny<CancellationToken>());
        }
    }
}
