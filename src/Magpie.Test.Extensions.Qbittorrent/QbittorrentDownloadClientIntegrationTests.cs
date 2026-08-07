using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Qbittorrent;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Sdk.Integrations;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

using RichardSzalay.MockHttp;

namespace Magpie.Test.Extensions.Qbittorrent;

[TestFixture]
public class QbittorrentDownloadClientIntegrationTests
{
    private const string Hash = "abcdef0123456789abcdef0123456789abcdef01";
    private const string Magnet = $"magnet:?xt=urn:btih:{Hash}&dn=test";

    private const string TorrentPayload =
        "d8:announce31:http://tracker.example/announce4:infod6:lengthi100e4:name8:test.bin" +
        "12:piece lengthi65536e6:pieces20:01234567890123456789ee";

    private const string TorrentInfoHash = "067315e2ae9e7c3287f7c338df2fdc6e8f551a5c";

    private static readonly IReadOnlyDictionary<string, string> AnonymousSettings =
        new Dictionary<string, string>
        {
            ["host"] = "http://qbit.local:8080"
        };

    private static readonly IReadOnlyDictionary<string, string> CredentialSettings =
        new Dictionary<string, string>
        {
            ["host"] = "http://qbit.local:8080",
            ["username"] = "admin",
            ["password"] = "secret"
        };

    private static readonly byte[] TorrentBytes = Encoding.ASCII.GetBytes(TorrentPayload);

    private Mock<IQbittorrentClient> _client = null!;
    private Mock<IHttpClientFactory> _httpClientFactory = null!;
    private MockHttpMessageHandler _torrentHandler = null!;
    private HttpClient _torrentHttp = null!;
    private IDownloadClientIntegration _integration = null!;

    [SetUp]
    public void SetUp()
    {
        _client = new Mock<IQbittorrentClient>(MockBehavior.Strict);
        _httpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        _torrentHandler = new MockHttpMessageHandler();
        _torrentHttp = new HttpClient(_torrentHandler, disposeHandler: false);
        _httpClientFactory
            .Setup(factory => factory.CreateClient(QbittorrentDownloadClientIntegration.TorrentFileHttpClientName))
            .Returns(_torrentHttp);
        _integration = new QbittorrentDownloadClientIntegration(
            NullLogger<QbittorrentDownloadClientIntegration>.Instance,
            _client.Object,
            _httpClientFactory.Object);
    }

    [TearDown]
    public void TearDown()
    {
        ((IDisposable)_integration).Dispose();
        _torrentHttp.Dispose();
        _torrentHandler.Dispose();
    }

    [Test]
    public void ProviderType_Identity_WhenIntegrationIsCreated()
    {
        // Arrange

        // Act
        var providerType = _integration.ProviderType;
        var protocols = _integration.SupportedProtocols.ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(providerType, Is.EqualTo("qbittorrent"));
            Assert.That(protocols, Is.EqualTo(["torrent"]));
            Assert.That(_integration, Is.InstanceOf<IPausableDownloadClient>());
            Assert.That(_integration, Is.InstanceOf<IResumableDownloadClient>());
            Assert.That(_integration, Is.InstanceOf<IForcableDownloadClient>());
        }
    }

    [Test]
    public async Task TestConnectionAsync_Success_WhenWebUiIsAnonymous()
    {
        // Arrange
        _client.Setup(client => client.GetVersionAsync(
                It.Is<QbittorrentSettings>(settings => settings.Host == new Uri("http://qbit.local:8080/")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("v4.6.0");

        // Act
        var result = await _integration.TestConnectionAsync(AnonymousSettings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Is.EqualTo("Connected to qBittorrent v4.6.0"));
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.None));
        }
    }

    [Test]
    public async Task TestConnectionAsync_Success_WhenCredentialsAreAccepted()
    {
        // Arrange
        _client.Setup(client => client.LoginAsync(
                It.Is<QbittorrentSettings>(settings => settings.Username == "admin" && settings.Password == "secret"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _client.Setup(client => client.GetVersionAsync(
                It.IsAny<QbittorrentSettings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("v5.0.0");

        // Act
        var result = await _integration.TestConnectionAsync(CredentialSettings, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task TestConnectionAsync_AuthFailure_WhenCredentialsAreRejected()
    {
        // Arrange
        _client.Setup(client => client.LoginAsync(
                It.IsAny<QbittorrentSettings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _integration.TestConnectionAsync(CredentialSettings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("Authentication"));
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.Auth));
        }
    }

    [Test]
    public async Task TestConnectionAsync_AuthFailure_WhenClientRequiresAuthentication()
    {
        // Arrange
        _client.Setup(client => client.GetVersionAsync(
                It.IsAny<QbittorrentSettings>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new QbittorrentAuthenticationException("authentication required"));

        // Act
        var result = await _integration.TestConnectionAsync(AnonymousSettings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("Authentication"));
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.Auth));
        }
    }

    [Test]
    public async Task TestConnectionAsync_FatalFailure_WhenRequestFails()
    {
        // Arrange
        _client.Setup(client => client.GetVersionAsync(
                It.IsAny<QbittorrentSettings>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        // Act
        var result = await _integration.TestConnectionAsync(AnonymousSettings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("connection refused"));
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.Fatal));
        }
    }

    [Test]
    public async Task TestConnectionAsync_TransientFailure_WhenRequestTimesOut()
    {
        // Arrange
        _client.Setup(client => client.GetVersionAsync(
                It.IsAny<QbittorrentSettings>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("request timeout"));

        // Act
        var result = await _integration.TestConnectionAsync(AnonymousSettings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("Timed out"));
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.Transient));
        }
    }

    [Test]
    public async Task TestConnectionAsync_FatalFailure_WhenHostIsMissing()
    {
        // Arrange
        var settings = new Dictionary<string, string>();

        // Act
        var result = await _integration.TestConnectionAsync(settings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("host"));
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.Fatal));
        }
    }

    [Test]
    public async Task DownloadReleaseAsync_GrabbedHash_WhenMagnetIsAdded()
    {
        // Arrange
        QbittorrentSettings? suppliedSettings = null;
        var suppliedTags = "unexpected";
        _client.Setup(client => client.AddMagnetAsync(
                It.IsAny<QbittorrentSettings>(),
                Magnet,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<QbittorrentSettings, string, string?, CancellationToken>((settings, _, tags, _) =>
            {
                suppliedSettings = settings;
                suppliedTags = tags;
            })
            .Returns(Task.CompletedTask);
        var release = new Release
        {
            Title = "Test Release",
            Protocol = "torrent",
            InfoUrl = new Uri(Magnet),
            Categories = ["Movies"]
        };

        // Act
        var result = await _integration.DownloadReleaseAsync(release, AnonymousSettings, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExternalId, Is.EqualTo(Hash));
            Assert.That(result.Status, Is.EqualTo(DownloadStatus.Grabbed));
            Assert.That(suppliedSettings!.Category, Is.EqualTo("magpie"));
            Assert.That(suppliedTags, Is.Null);
        }
    }

    [TestCase(null, "magpie", null)]
    [TestCase("films", "films", null)]
    [TestCase(null, "magpie", "movie")]
    [TestCase("films", "films", "documentary")]
    public async Task DownloadReleaseAsync_SettingsAndTags_WhenMagnetIsAdded(
        string? configuredCategory,
        string expectedCategory,
        string? contentType)
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["host"] = "http://qbit.local:8080"
        };
        if (configuredCategory is not null)
        {
            settings["category"] = configuredCategory;
        }

        QbittorrentSettings? suppliedSettings = null;
        string? suppliedTags = null;
        _client.Setup(client => client.AddMagnetAsync(
                It.IsAny<QbittorrentSettings>(),
                Magnet,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<QbittorrentSettings, string, string?, CancellationToken>((parsed, _, tags, _) =>
            {
                suppliedSettings = parsed;
                suppliedTags = tags;
            })
            .Returns(Task.CompletedTask);
        var release = new Release
        {
            Title = "Tagged Release",
            Protocol = "torrent",
            InfoUrl = new Uri(Magnet),
            Categories = ["TV", "HD"]
        };
        var context = new ReleaseGrabContext { ContentType = contentType };

        // Act
        await _integration.DownloadReleaseAsync(release, context, settings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(suppliedSettings!.Category, Is.EqualTo(expectedCategory));
            Assert.That(suppliedTags, Is.EqualTo(contentType));
        }
    }

    [Test]
    public async Task DownloadReleaseAsync_ExistingTorrent_WhenDuplicateMagnetIsRejected()
    {
        // Arrange
        _client.Setup(client => client.AddMagnetAsync(
                It.IsAny<QbittorrentSettings>(),
                Magnet,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new QbittorrentAddRejectedException("duplicate", "Fails."));
        _client.Setup(client => client.GetTorrentInfoAsync(
                It.IsAny<QbittorrentSettings>(),
                Hash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new TorrentInfo
                {
                    Hash = Hash,
                    State = "error",
                    ContentPath = "C:\\downloads\\release"
                }
            ]);
        var release = new Release
        {
            Title = "Duplicate Release",
            Protocol = "torrent",
            InfoUrl = new Uri(Magnet)
        };

        // Act
        var result = await _integration.DownloadReleaseAsync(release, AnonymousSettings, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExternalId, Is.EqualTo(Hash));
            Assert.That(result.Status, Is.EqualTo(DownloadStatus.Grabbed));
            Assert.That(result.StatusReason, Is.Null);
            Assert.That(result.OutputPath, Is.EqualTo("C:\\downloads\\release"));
        }
    }

    [Test]
    public void DownloadReleaseAsync_Throws_WhenDuplicateMagnetIsNotFound()
    {
        // Arrange
        _client.Setup(client => client.AddMagnetAsync(
                It.IsAny<QbittorrentSettings>(),
                Magnet,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new QbittorrentAddRejectedException("duplicate", "Fails."));
        _client.Setup(client => client.GetTorrentInfoAsync(
                It.IsAny<QbittorrentSettings>(),
                Hash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var release = new Release
        {
            Title = "Missing Duplicate",
            Protocol = "torrent",
            InfoUrl = new Uri(Magnet)
        };

        // Act
        var exception = Assert.ThrowsAsync<QbittorrentAddRejectedException>(
            async () => await _integration.DownloadReleaseAsync(release, AnonymousSettings, default)
                .ConfigureAwait(false));

        // Assert
        Assert.That(exception!.ResponseBody, Is.EqualTo("Fails."));
    }

    [Test]
    public async Task DownloadReleaseAsync_NameLookupHash_WhenMagnetHasNoV1Hash()
    {
        // Arrange
        var v2Magnet = "magnet:?xt=urn:btmh:1220abcdef0123456789abcdef0123456789" +
            "abcdef0123456789abcdef0123456789&dn=test";
        _client.Setup(client => client.AddMagnetAsync(
                It.IsAny<QbittorrentSettings>(),
                v2Magnet,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _client.Setup(client => client.FindTorrentByNameAsync(
                It.IsAny<QbittorrentSettings>(),
                "V2 Release",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TorrentInfo { Hash = Hash, Name = "V2 Release" });
        var release = new Release
        {
            Title = "V2 Release",
            Protocol = "torrent",
            InfoUrl = new Uri(v2Magnet)
        };

        // Act
        var result = await _integration.DownloadReleaseAsync(release, AnonymousSettings, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.ExternalId, Is.EqualTo(Hash));
    }

    [Test]
    public void DownloadReleaseAsync_Throws_WhenRejectedMagnetHasNoV1Hash()
    {
        // Arrange
        var v2Magnet = "magnet:?xt=urn:btmh:1220abcdef0123456789abcdef0123456789" +
            "abcdef0123456789abcdef0123456789&dn=test";
        _client.Setup(client => client.AddMagnetAsync(
                It.IsAny<QbittorrentSettings>(),
                v2Magnet,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new QbittorrentAddRejectedException("rejected", "Fails."));
        var release = new Release
        {
            Title = "Rejected V2 Release",
            Protocol = "torrent",
            InfoUrl = new Uri(v2Magnet)
        };

        // Act
        var exception = Assert.ThrowsAsync<QbittorrentAddRejectedException>(
            async () => await _integration.DownloadReleaseAsync(release, AnonymousSettings, default)
                .ConfigureAwait(false));

        // Assert
        Assert.That(exception!.ResponseBody, Is.EqualTo("Fails."));
    }

    [Test]
    public async Task DownloadReleaseAsync_TorrentHash_WhenTorrentUrlIsDownloaded()
    {
        // Arrange
        byte[]? suppliedBytes = null;
        string? suppliedFileName = null;
        QbittorrentSettings? suppliedSettings = null;
        _torrentHandler.Expect(HttpMethod.Get, "https://tracker.example/download/file.torrent")
            .Respond(HttpStatusCode.OK, _ => new ByteArrayContent(TorrentBytes));
        _client.Setup(client => client.AddTorrentFileAsync(
                It.IsAny<QbittorrentSettings>(),
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<QbittorrentSettings, byte[], string, string?, CancellationToken>(
                (settings, bytes, fileName, _, _) =>
                {
                    suppliedSettings = settings;
                    suppliedBytes = bytes;
                    suppliedFileName = fileName;
                })
            .Returns(Task.CompletedTask);
        var release = new Release
        {
            Title = "HTTP Release",
            Protocol = "torrent",
            InfoUrl = new Uri("https://tracker.example/download/file.torrent"),
            Categories = ["Movies"]
        };

        // Act
        var result = await _integration.DownloadReleaseAsync(release, AnonymousSettings, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExternalId, Is.EqualTo(TorrentInfoHash));
            Assert.That(result.Status, Is.EqualTo(DownloadStatus.Grabbed));
            Assert.That(suppliedBytes, Is.EqualTo(TorrentBytes));
            Assert.That(suppliedFileName, Is.EqualTo("file.torrent"));
            Assert.That(suppliedSettings!.Category, Is.EqualTo("magpie"));
        }

        _torrentHandler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task DownloadReleaseAsync_ReusesTorrentHttpClient_WhenMultipleTorrentsAreDownloaded()
    {
        // Arrange
        var downloadRequest = _torrentHandler
            .When(HttpMethod.Get, "https://tracker.example/download/reused.torrent")
            .Respond(HttpStatusCode.OK, _ => new ByteArrayContent(TorrentBytes));
        _client.Setup(client => client.AddTorrentFileAsync(
                It.IsAny<QbittorrentSettings>(),
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var release = new Release
        {
            Title = "Reused Client Release",
            Protocol = "torrent",
            InfoUrl = new Uri("https://tracker.example/download/reused.torrent")
        };

        // Act
        await _integration.DownloadReleaseAsync(release, AnonymousSettings, default).ConfigureAwait(false);
        await _integration.DownloadReleaseAsync(release, AnonymousSettings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_torrentHandler.GetMatchCount(downloadRequest), Is.EqualTo(2));
            _httpClientFactory.Verify(
                factory => factory.CreateClient(
                    QbittorrentDownloadClientIntegration.TorrentFileHttpClientName),
                Times.Once);
            _client.Verify(client => client.AddTorrentFileAsync(
                It.IsAny<QbittorrentSettings>(),
                It.IsAny<byte[]>(),
                "reused.torrent",
                null,
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }

    [Test]
    public async Task DownloadReleaseAsync_NameLookupHash_WhenTorrentCannotBeParsed()
    {
        // Arrange
        var fallbackHash = "1111111111111111111111111111111111111111";
        _torrentHandler.Expect(HttpMethod.Get, "https://tracker.example/bogus.torrent")
            .Respond(HttpStatusCode.OK, _ => new ByteArrayContent([0x00, 0x01, 0x02, 0x03]));
        _client.Setup(client => client.AddTorrentFileAsync(
                It.IsAny<QbittorrentSettings>(),
                It.IsAny<byte[]>(),
                "bogus.torrent",
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _client.Setup(client => client.FindTorrentByNameAsync(
                It.IsAny<QbittorrentSettings>(),
                "Bogus Release",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TorrentInfo { Hash = fallbackHash, Name = "Bogus Release" });
        var release = new Release
        {
            Title = "Bogus Release",
            Protocol = "torrent",
            InfoUrl = new Uri("https://tracker.example/bogus.torrent")
        };

        // Act
        var result = await _integration.DownloadReleaseAsync(release, AnonymousSettings, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.ExternalId, Is.EqualTo(fallbackHash));
    }

    [Test]
    public void DownloadReleaseAsync_Throws_WhenUnparseableTorrentIsNotFound()
    {
        // Arrange
        _torrentHandler.Expect(HttpMethod.Get, "https://tracker.example/bogus.torrent")
            .Respond(HttpStatusCode.OK, _ => new ByteArrayContent([0x00, 0x01, 0x02, 0x03]));
        _client.Setup(client => client.AddTorrentFileAsync(
                It.IsAny<QbittorrentSettings>(),
                It.IsAny<byte[]>(),
                "bogus.torrent",
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _client.Setup(client => client.FindTorrentByNameAsync(
                It.IsAny<QbittorrentSettings>(),
                "Bogus Release",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TorrentInfo?)null);
        var release = new Release
        {
            Title = "Bogus Release",
            Protocol = "torrent",
            InfoUrl = new Uri("https://tracker.example/bogus.torrent")
        };

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _integration.DownloadReleaseAsync(release, AnonymousSettings, default)
                .ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Does.Contain("Failed to determine"));
    }

    [TestCase(null)]
    [TestCase("ftp://tracker.example/file.torrent")]
    public void DownloadReleaseAsync_Throws_WhenInfoUrlIsUnsupported(string? infoUrl)
    {
        // Arrange
        var release = new Release
        {
            Title = "Unsupported Release",
            Protocol = "torrent",
            InfoUrl = infoUrl is null ? null : new Uri(infoUrl)
        };

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _integration.DownloadReleaseAsync(release, AnonymousSettings, default)
                .ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Does.Contain(infoUrl is null ? "no InfoUrl" : "ftp"));
    }

    [Test]
    public async Task GetDownloadAsync_Failed_WhenTorrentIsNotFound()
    {
        // Arrange
        _client.Setup(client => client.GetTorrentInfoAsync(
                It.IsAny<QbittorrentSettings>(),
                Hash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _integration.GetDownloadAsync(Hash, AnonymousSettings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExternalId, Is.EqualTo(Hash));
            Assert.That(result.Status, Is.EqualTo(DownloadStatus.Failed));
            Assert.That(result.StatusReason, Is.EqualTo("Torrent not found in client"));
        }
    }

    [Test]
    public async Task GetDownloadAsync_SavePath_WhenContentPathIsMissing()
    {
        // Arrange
        _client.Setup(client => client.GetTorrentInfoAsync(
                It.IsAny<QbittorrentSettings>(),
                Hash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new TorrentInfo
                {
                    Hash = Hash,
                    State = "downloading",
                    SavePath = "/downloads"
                }
            ]);

        // Act
        var result = await _integration.GetDownloadAsync(Hash, AnonymousSettings, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.OutputPath, Is.EqualTo("/downloads"));
    }

    [TestCase("error", DownloadStatus.Failed, "qBittorrent state: error", false)]
    [TestCase("downloading", DownloadStatus.Downloading, null, false)]
    [TestCase("pausedDL", DownloadStatus.Paused, null, false)]
    [TestCase("queuedDL", DownloadStatus.Queued, null, false)]
    [TestCase(
        "stalledDL",
        DownloadStatus.Stalled,
        "qBittorrent state: stalledDL (no connected seeds/peers)",
        false)]
    [TestCase("seeding", DownloadStatus.Downloaded, null, true)]
    public async Task GetDownloadAsync_Status_WhenTorrentStateIsMapped(
        string state,
        DownloadStatus expectedStatus,
        string? expectedReason,
        bool expectedHeldByPolicy)
    {
        // Arrange
        _client.Setup(client => client.GetTorrentInfoAsync(
                It.IsAny<QbittorrentSettings>(),
                Hash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new TorrentInfo
                {
                    Hash = Hash,
                    State = state,
                    SavePath = "C:\\downloads",
                    ContentPath = "C:\\downloads\\release",
                    Progress = 0.5,
                    Ratio = 0.5,
                    MaxRatio = 2,
                    SeedingTime = 60,
                    MaxSeedingTime = 120
                }
            ]);

        // Act
        var result = await _integration.GetDownloadAsync(Hash, AnonymousSettings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(expectedStatus));
            Assert.That(result.StatusReason, Is.EqualTo(expectedReason));
            Assert.That(result.OutputPath, Is.EqualTo("C:\\downloads\\release"));
            Assert.That(result.ProgressPercent, Is.EqualTo(50m));
            Assert.That(result.HeldByPolicy, Is.EqualTo(expectedHeldByPolicy));
        }
    }

    [TestCase(0.0, 2.0, 0L, 120L, true)]
    [TestCase(2.0, 2.0, 0L, 120L, false)]
    [TestCase(0.0, 2.0, 7200L, 120L, false)]
    [TestCase(0.0, -1.0, 0L, -1L, true)]
    public async Task GetDownloadAsync_HeldByPolicy_WhenSeedingLimitsAreEvaluated(
        double ratio,
        double maxRatio,
        long seedingTime,
        long maxSeedingTime,
        bool expected)
    {
        // Arrange
        _client.Setup(client => client.GetTorrentInfoAsync(
                It.IsAny<QbittorrentSettings>(),
                Hash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new TorrentInfo
                {
                    Hash = Hash,
                    State = "seeding",
                    Ratio = ratio,
                    MaxRatio = maxRatio,
                    SeedingTime = seedingTime,
                    MaxSeedingTime = maxSeedingTime
                }
            ]);

        // Act
        var result = await _integration.GetDownloadAsync(Hash, AnonymousSettings, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.HeldByPolicy, Is.EqualTo(expected));
    }

    [TestCase(0.0, 0)]
    [TestCase(0.5, 50)]
    [TestCase(1.0, 100)]
    public async Task GetDownloadAsync_ProgressPercent_WhenProgressIsMapped(double progress, int expected)
    {
        // Arrange
        _client.Setup(client => client.GetTorrentInfoAsync(
                It.IsAny<QbittorrentSettings>(),
                Hash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TorrentInfo { Hash = Hash, State = "downloading", Progress = progress }]);

        // Act
        var result = await _integration.GetDownloadAsync(Hash, AnonymousSettings, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.ProgressPercent, Is.EqualTo(expected));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task RemoveDownloadAsync_Result_WhenClientReturns(bool expected)
    {
        // Arrange
        _client.Setup(client => client.DeleteAsync(
                It.Is<QbittorrentSettings>(settings => settings.Host == new Uri("http://qbit.local:8080/")),
                Hash,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _integration.RemoveDownloadAsync(Hash, false, AnonymousSettings, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task PauseDownloadAsync_Result_WhenClientReturns(bool expected)
    {
        // Arrange
        _client.Setup(client => client.PauseAsync(
                It.Is<QbittorrentSettings>(settings => settings.Host == new Uri("http://qbit.local:8080/")),
                Hash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await ((IPausableDownloadClient)_integration)
            .PauseDownloadAsync(Hash, AnonymousSettings, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ResumeDownloadAsync_Result_WhenClientReturns(bool expected)
    {
        // Arrange
        _client.Setup(client => client.ResumeAsync(
                It.Is<QbittorrentSettings>(settings => settings.Host == new Uri("http://qbit.local:8080/")),
                Hash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await ((IResumableDownloadClient)_integration)
            .ResumeDownloadAsync(Hash, AnonymousSettings, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ForceStartDownloadAsync_Result_WhenClientReturns(bool expected)
    {
        // Arrange
        _client.Setup(client => client.SetForceStartAsync(
                It.Is<QbittorrentSettings>(settings => settings.Host == new Uri("http://qbit.local:8080/")),
                Hash,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await ((IForcableDownloadClient)_integration)
            .ForceStartDownloadAsync(Hash, AnonymousSettings, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void SettingsSchema_Keys_WhenSchemaIsRead()
    {
        // Arrange

        // Act
        var keys = _integration.SettingsSchema.Select(definition => definition.Key).ToArray();

        // Assert
        Assert.That(
            keys,
            Is.EquivalentTo(
            [
                "host",
                "username",
                "password",
                "category",
                "paused",
                DownloadClientSettingsKeys.HonorClientHoldPolicy
            ]));
    }

    [TestCase("host", DataType.String, true, false, null)]
    [TestCase("username", DataType.String, false, false, null)]
    [TestCase("password", DataType.String, false, true, null)]
    [TestCase("category", DataType.String, false, false, "magpie")]
    [TestCase("paused", DataType.Boolean, false, false, "false")]
    [TestCase(DownloadClientSettingsKeys.HonorClientHoldPolicy, DataType.Boolean, false, false, "true")]
    public void SettingsSchema_Definition_WhenKeyIsRead(
        string key,
        DataType type,
        bool required,
        bool secret,
        string? defaultValue)
    {
        // Arrange

        // Act
        var definition = _integration.SettingsSchema.Single(item => item.Key == key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(definition.Type, Is.EqualTo(type));
            Assert.That(definition.Required, Is.EqualTo(required));
            Assert.That(definition.Secret, Is.EqualTo(secret));
            Assert.That(definition.DefaultValue, Is.EqualTo(defaultValue));
        }
    }

    [TestCase("http://localhost:8080")]
    [TestCase("http://localhost:8080/qbittorrent")]
    [TestCase("https://qbit.example.com:9000")]
    public async Task ValidateSettingsAsync_Valid_WhenHostIsHttp(string host)
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["host"] = host,
            ["username"] = "admin",
            ["password"] = "secret",
            ["category"] = "magpie",
            ["paused"] = "true"
        };

        // Act
        var result = await ((IValidatesIntegrationSettings)_integration)
            .ValidateSettingsAsync(settings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Is.Empty);
        }
    }

    [TestCase("not a url")]
    [TestCase("/relative/only")]
    [TestCase("ftp://qbit.example.com")]
    public async Task ValidateSettingsAsync_HostIssue_WhenHostIsInvalid(string host)
    {
        // Arrange
        var settings = new Dictionary<string, string> { ["host"] = host };

        // Act
        var result = await ((IValidatesIntegrationSettings)_integration)
            .ValidateSettingsAsync(settings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.Issues,
                Has.Some
                    .Property("Key").EqualTo("host")
                    .And.Property("Message").EqualTo("Host must be an absolute http(s) URL."));
        }
    }

    [TestCase("admin", "", "password")]
    [TestCase("", "secret", "username")]
    public async Task ValidateSettingsAsync_CredentialIssue_WhenCredentialIsMissing(
        string username,
        string password,
        string issueKey)
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["host"] = "http://localhost:8080",
            ["username"] = username,
            ["password"] = password
        };

        // Act
        var result = await ((IValidatesIntegrationSettings)_integration)
            .ValidateSettingsAsync(settings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Some.Property("Key").EqualTo(issueKey));
        }
    }

    [Test]
    public async Task ValidateSettingsAsync_PasswordIssue_WhenPasswordKeyIsMissing()
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["host"] = "http://localhost:8080",
            ["username"] = "admin"
        };

        // Act
        var result = await ((IValidatesIntegrationSettings)_integration)
            .ValidateSettingsAsync(settings, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Issues, Has.Some.Property("Key").EqualTo("password"));
    }
}
